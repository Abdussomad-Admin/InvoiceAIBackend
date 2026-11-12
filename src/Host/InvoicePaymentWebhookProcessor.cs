using System.Text.Json;
using Domain.Billing;
using Domain.Invoices;
using Domain.Organizations;
using Domain.Payments;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Host;

public sealed class InvoicePaymentWebhookProcessor(
    AppDbContext db, IHttpClientFactory clients, PaystackSettings paystack, TimeProvider clock)
{
    public async Task ProcessSuccessfulCharge(JsonElement data, CancellationToken ct)
    {
        if (!TryString(data, "reference", out var reference) || !reference.StartsWith("invpay_", StringComparison.Ordinal)) return;
        var checkout = await db.InvoicePaymentCheckouts.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.Reference == reference, ct);
        if (checkout is null || checkout.Status == InvoicePaymentCheckoutStatus.Succeeded) return;
        using var client = clients.CreateClient("Paystack");
        using var verifyRequest = new HttpRequestMessage(HttpMethod.Get, $"transaction/verify/{Uri.EscapeDataString(reference)}");
        verifyRequest.Headers.Authorization = new("Bearer", paystack.SecretKey);
        using var verifyResponse = await client.SendAsync(verifyRequest, ct);
        if (!verifyResponse.IsSuccessStatusCode) throw new HttpRequestException("Paystack transaction verification failed.");
        using var verifiedDoc = JsonDocument.Parse(await verifyResponse.Content.ReadAsStringAsync(ct));
        if (!verifiedDoc.RootElement.TryGetProperty("data", out var verified)
            || !TryString(verified, "status", out var status) || status != "success"
            || !TryString(verified, "reference", out var verifiedReference) || verifiedReference != reference
            || !TryLong(verified, "amount", out var amount) || amount != checkout.AmountMinor
            || !TryString(verified, "currency", out var currency)
            || !string.Equals(currency, checkout.CurrencyCode, StringComparison.OrdinalIgnoreCase)
            || !TryString(verified, "subaccount_code", out var subaccountCode)) return;
        var account = await db.OrganizationPaystackAccounts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(a => a.OrganizationId == checkout.OrganizationId, ct);
        var testMode = paystack.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal);
        if (account is null || !account.IsActive || (!account.IsVerified && !testMode) || account.SubaccountCode != subaccountCode) return;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        checkout = await db.InvoicePaymentCheckouts.IgnoreQueryFilters().SingleAsync(c => c.Id == checkout.Id, ct);
        if (checkout.Status == InvoicePaymentCheckoutStatus.Succeeded) return;
        await db.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM \"Invoices\" WHERE \"Id\" = {checkout.InvoiceId} AND \"OrganizationId\" = {checkout.OrganizationId} FOR UPDATE").SingleOrDefaultAsync(ct);
        var invoice = await db.Invoices.IgnoreQueryFilters().SingleOrDefaultAsync(i => i.Id == checkout.InvoiceId && i.OrganizationId == checkout.OrganizationId, ct);
        if (invoice is null || invoice.Balance <= 0 || invoice.CurrencyCode != checkout.CurrencyCode) return;
        var paidAmount = checkout.AmountMinor / 100m;
        if (paidAmount > invoice.Balance) return;
        var owner = await db.Memberships.IgnoreQueryFilters()
            .Where(m => m.OrganizationId == checkout.OrganizationId && m.IsActive && m.Role == OrganizationRole.Owner)
            .Select(m => (Guid?)m.UserId).FirstOrDefaultAsync(ct);
        if (owner is null) throw new InvalidOperationException("No active owner is available to record the payment.");
        invoice.ApplyPayment(paidAmount);
        db.Payments.Add(Payment.Create(checkout.OrganizationId, invoice.Id, invoice.CustomerId,
            invoice.CurrencyCode, paidAmount, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            PaymentMethod.Card, reference, "Verified Paystack invoice payment", owner.Value));
        checkout.MarkSucceeded();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static bool TryString(JsonElement value, string key, out string result)
    {
        result = "";
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var element)
               && element.ValueKind == JsonValueKind.String && (result = element.GetString() ?? "").Length > 0;
    }

    private static bool TryLong(JsonElement value, string key, out long result)
    {
        result = 0;
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var element) && element.TryGetInt64(out result);
    }
}
