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
        await ProcessSuccessfulReference(reference, ct);
    }

    public async Task ProcessSuccessfulReference(string reference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reference) || !reference.StartsWith("invpay_", StringComparison.Ordinal)) return;
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
            || !string.Equals(currency, checkout.CurrencyCode, StringComparison.OrdinalIgnoreCase)) return;
        var hasSubaccountCode = TryString(verified, "subaccount_code", out var subaccountCode)
                                || TryNestedString(verified, "subaccount", "subaccount_code", out subaccountCode);
        var account = await db.OrganizationPaystackAccounts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(a => a.OrganizationId == checkout.OrganizationId, ct);
        var testMode = paystack.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal);
        if (account is null || !account.IsActive || (!account.IsVerified && !testMode)
            || (hasSubaccountCode && account.SubaccountCode != subaccountCode)) return;

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

    public async Task ProcessRefundUpdate(JsonElement data, string eventName, CancellationToken ct)
    {
        if (!TryString(data, "refund_reference", out var reference)) return;
        var refund = await db.CreditNoteRefunds.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.ProviderReference == reference && x.Channel == CreditNoteRefundChannel.Paystack, ct);
        if (refund is null || !TryString(data, "transaction_reference", out var transactionReference)) return;
        var payment = await db.Payments.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == refund.PaymentId && x.OrganizationId == refund.OrganizationId, ct);
        if (payment is null || payment.Reference != transactionReference) return;
        if (TryString(data, "currency", out var currency)
            && !string.Equals(currency, refund.CurrencyCode, StringComparison.OrdinalIgnoreCase)) return;
        if (TryAmountMinor(data, "amount", out var amountMinor)
            && amountMinor != decimal.ToInt64(decimal.Round(refund.Amount * 100m, 0, MidpointRounding.AwayFromZero))) return;

        var status = eventName.StartsWith("refund.", StringComparison.Ordinal)
            ? eventName["refund.".Length..]
            : TryString(data, "status", out var returnedStatus) ? returnedStatus : "pending";
        refund.ApplyProviderStatus(status, null, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }

    private static bool TryString(JsonElement value, string key, out string result)
    {
        result = "";
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var element)
               && element.ValueKind == JsonValueKind.String && (result = element.GetString() ?? "").Length > 0;
    }

    private static bool TryNestedString(JsonElement value, string parentKey, string key, out string result)
    {
        result = "";
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(parentKey, out var parent)
               && TryString(parent, key, out result);
    }

    private static bool TryLong(JsonElement value, string key, out long result)
    {
        result = 0;
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var element) && element.TryGetInt64(out result);
    }

    private static bool TryAmountMinor(JsonElement value, string key, out long result)
    {
        result = 0;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out var element)) return false;
        return element.ValueKind == JsonValueKind.Number
            ? element.TryGetInt64(out result)
            : element.ValueKind == JsonValueKind.String
              && long.TryParse(element.GetString(), System.Globalization.NumberStyles.None,
                  System.Globalization.CultureInfo.InvariantCulture, out result);
    }
}
