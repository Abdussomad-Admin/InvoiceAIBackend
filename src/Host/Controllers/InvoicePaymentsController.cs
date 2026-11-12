using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Common;
using Application.Common.Interfaces;
using Domain.Billing;
using Domain.Invoices;
using Domain.Organizations;
using Domain.Payments;
using Host;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Host.Controllers;

[ApiController]
public sealed class InvoicePaymentsController(
    AppDbContext db,
    ICurrentOrganization currentOrganization,
    IHttpClientFactory clients,
    PaystackSettings paystack,
    AppLinkSettings links,
    TimeProvider clock,
    InvoicePaymentWebhookProcessor invoicePaymentWebhook,
    ILogger<InvoicePaymentsController> logger) : ControllerBase
{
    [HttpGet("api/payment-accounts/paystack")]
    [Authorize(Policy = nameof(Permission.ManageOrganization))]
    public async Task<IActionResult> Account(CancellationToken ct)
    {
        var account = await db.OrganizationPaystackAccounts.AsNoTracking().SingleOrDefaultAsync(ct);
        return account is null ? Ok(new { status = "Not set up" }) : Ok(new
        {
            status = account.IsActive && account.IsVerified ? "Ready" : "Pending verification",
            account.CurrencyCode, account.IsActive, account.IsVerified, account.LastCheckedAt
        });
    }

    [HttpPost("api/payment-accounts/paystack")]
    [Authorize(Policy = nameof(Permission.ManageOrganization))]
    public async Task<IActionResult> CreateAccount(CreatePaystackAccountRequest input, CancellationToken ct)
    {
        if (!paystack.Enabled || string.IsNullOrWhiteSpace(paystack.SecretKey))
            return Problem("Online payments are not configured yet.", statusCode: 503);
        var organizationId = currentOrganization.OrganizationId;
        if (organizationId is null) return BadRequest(new { error = "No active business workspace." });
        if (await db.OrganizationPaystackAccounts.AnyAsync(ct))
            return Conflict(new { error = "A Paystack account is already linked to this business." });
        if (input.AccountNumber.Length is < 8 or > 20 || input.BankCode.Length is < 2 or > 20
            || paystack.PlatformPercentageCharge is < 0 or > 100)
            return BadRequest(new { error = "Enter valid bank details." });

        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == organizationId, ct);
        if (organization is null) return NotFound();
        using var client = clients.CreateClient("Paystack");
        using var request = new HttpRequestMessage(HttpMethod.Post, "subaccount");
        request.Headers.Authorization = new("Bearer", paystack.SecretKey);
        request.Content = JsonContent.Create(new
        {
            business_name = organization.Name,
            settlement_bank = input.BankCode.Trim(),
            account_number = input.AccountNumber.Trim(),
            percentage_charge = paystack.PlatformPercentageCharge,
            primary_contact_email = organization.Email,
            primary_contact_phone = organization.Phone
        });
        try
        {
            using var response = await client.SendAsync(request, ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!response.IsSuccessStatusCode || !json.RootElement.TryGetProperty("data", out var data)
                || !TryString(data, "subaccount_code", out var code))
            {
                logger.LogWarning("Paystack subaccount creation failed with HTTP {StatusCode}.", response.StatusCode);
                return Problem("Paystack could not create the business payment account. Check the bank details and try again.", statusCode: 502);
            }
            var currency = TryString(data, "currency", out var returnedCurrency)
                ? returnedCurrency : organization.BaseCurrencyCode;
            var account = OrganizationPaystackAccount.Create(organizationId.Value, code, currency);
            account.RefreshProviderStatus(
                data.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True,
                data.TryGetProperty("is_verified", out var verified) && verified.ValueKind == JsonValueKind.True,
                clock.GetUtcNow());
            db.OrganizationPaystackAccounts.Add(account);
            await db.SaveChangesAsync(ct);
            return Ok(new { status = account.IsActive && account.IsVerified ? "Ready" : "Pending verification", account.CurrencyCode });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not reach Paystack while creating a subaccount.");
            return Problem("Could not reach Paystack. Please try again later.", statusCode: 502);
        }
    }

    [HttpPost("api/payment-accounts/paystack/refresh")]
    [Authorize(Policy = nameof(Permission.ManageOrganization))]
    public async Task<IActionResult> RefreshAccount(CancellationToken ct)
    {
        if (!paystack.Enabled || string.IsNullOrWhiteSpace(paystack.SecretKey)) return Problem("Online payments are not configured yet.", statusCode: 503);
        var account = await db.OrganizationPaystackAccounts.SingleOrDefaultAsync(ct);
        if (account is null) return NotFound(new { error = "Set up a Paystack account first." });
        using var client = clients.CreateClient("Paystack");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"subaccount/{Uri.EscapeDataString(account.SubaccountCode)}");
        request.Headers.Authorization = new("Bearer", paystack.SecretKey);
        using var response = await client.SendAsync(request, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!response.IsSuccessStatusCode || !json.RootElement.TryGetProperty("data", out var data))
            return Problem("Paystack could not confirm this account yet.", statusCode: 502);
        var currency = TryString(data, "currency", out var code) ? code : account.CurrencyCode;
        var active = data.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.True;
        var verified = data.TryGetProperty("is_verified", out var v) && v.ValueKind == JsonValueKind.True;
        account.RefreshProviderStatus(active && string.Equals(currency, account.CurrencyCode, StringComparison.OrdinalIgnoreCase), verified, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return Ok(new { status = account.IsActive && account.IsVerified ? "Ready" : "Pending verification", account.CurrencyCode, account.IsActive, account.IsVerified, account.LastCheckedAt });
    }

    [HttpGet("api/public/invoices/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> PublicInvoice(string token, CancellationToken ct)
    {
        var invoice = await db.Invoices.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(i => i.PublicPaymentToken == token, ct);
        if (invoice is null || invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Void) return NotFound();
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == invoice.OrganizationId, ct);
        var customer = await db.Customers.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(c => c.Id == invoice.CustomerId && c.OrganizationId == invoice.OrganizationId, ct);
        if (organization is null || customer is null) return NotFound();
        return Ok(new { invoice.Number, businessName = organization.Name, customerName = customer.Name,
            invoice.CurrencyCode, invoice.Total, invoice.AmountPaid, invoice.Balance, invoice.DueDate,
            status = invoice.Status.ToString(), canPay = invoice.Balance > 0 && invoice.Status is InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid });
    }

    [HttpPost("api/public/invoices/{token}/checkout")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> StartCheckout(string token, CancellationToken ct)
    {
        if (!paystack.Enabled || string.IsNullOrWhiteSpace(paystack.SecretKey)) return Problem("Online payments are not available yet.", statusCode: 503);
        var invoice = await db.Invoices.IgnoreQueryFilters().SingleOrDefaultAsync(i => i.PublicPaymentToken == token, ct);
        if (invoice is null || invoice.Status is not (InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid) || invoice.Balance <= 0) return NotFound();
        var account = await db.OrganizationPaystackAccounts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(a => a.OrganizationId == invoice.OrganizationId, ct);
        if (account is null || !account.IsActive || !account.IsVerified || account.CurrencyCode != invoice.CurrencyCode)
            return Conflict(new { error = "This business is still completing payment account verification." });
        var customer = await db.Customers.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == invoice.CustomerId && c.OrganizationId == invoice.OrganizationId, ct);
        if (customer?.Email is null) return Conflict(new { error = "The business needs to add an email address for this customer before accepting online payment." });

        var previous = await db.InvoicePaymentCheckouts.IgnoreQueryFilters()
            .Where(c => c.InvoiceId == invoice.Id && c.Status == InvoicePaymentCheckoutStatus.Pending)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
        if (previous is not null && previous.CreatedAt > clock.GetUtcNow().AddMinutes(-30))
        {
            if (!string.IsNullOrWhiteSpace(previous.AuthorizationUrl))
                return Ok(new { reference = previous.Reference, authorizationUrl = previous.AuthorizationUrl });
            return Conflict(new { error = "A payment is already being prepared. Please wait a moment and try again." });
        }
        previous?.MarkFailed();

        var minorUnits = decimal.ToInt64(decimal.Round(invoice.Balance * 100m, 0, MidpointRounding.AwayFromZero));
        var reference = $"invpay_{Guid.CreateVersion7():N}";
        var checkout = InvoicePaymentCheckout.Create(invoice.OrganizationId, invoice.Id, reference, invoice.CurrencyCode, minorUnits);
        db.InvoicePaymentCheckouts.Add(checkout);
        await db.SaveChangesAsync(ct);
        using var client = clients.CreateClient("Paystack");
        using var request = new HttpRequestMessage(HttpMethod.Post, "transaction/initialize");
        request.Headers.Authorization = new("Bearer", paystack.SecretKey);
        request.Content = JsonContent.Create(new
        {
            email = customer.Email,
            amount = minorUnits,
            currency = invoice.CurrencyCode,
            reference,
            subaccount = account.SubaccountCode,
            callback_url = $"{links.FrontendBaseUrl.TrimEnd('/')}/pay/{Uri.EscapeDataString(token)}",
            metadata = new { purpose = "invoice_payment", invoiceId = invoice.Id, organizationId = invoice.OrganizationId }
        });
        using var response = await client.SendAsync(request, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!response.IsSuccessStatusCode || !json.RootElement.TryGetProperty("data", out var data)
            || !TryString(data, "authorization_url", out var authorizationUrl))
        {
            checkout.MarkFailed();
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Paystack invoice checkout initialization failed with HTTP {StatusCode}.", response.StatusCode);
            return Problem("Could not start the payment. Please try again.", statusCode: 502);
        }
        checkout.SetAuthorizationUrl(authorizationUrl);
        await db.SaveChangesAsync(ct);
        return Ok(new { reference, authorizationUrl });
    }

    [HttpPost("api/payments/paystack/webhook")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        if (!paystack.Enabled || string.IsNullOrWhiteSpace(paystack.SecretKey)) return NotFound();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync(ct);
        if (!ValidSignature(raw, Request.Headers["Paystack-Signature"].ToString(), paystack.WebhookSecret)
            && !ValidLegacySignature(raw, Request.Headers["x-paystack-signature"].ToString(), paystack.SecretKey)) return Unauthorized();
        using var payload = JsonDocument.Parse(raw);
        if (!payload.RootElement.TryGetProperty("event", out var eventName) || eventName.GetString() != "charge.success"
            || !payload.RootElement.TryGetProperty("data", out var data)
            || !TryString(data, "reference", out var reference) || !reference.StartsWith("invpay_", StringComparison.Ordinal)) return Ok();
        await invoicePaymentWebhook.ProcessSuccessfulCharge(data, ct);
        return Ok();
    }

    private bool ValidSignature(string raw, string header, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return false;
        var parts = header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var timestampPart = parts.SingleOrDefault(x => x.StartsWith("t=", StringComparison.Ordinal));
        var signatures = parts.Where(x => x.StartsWith("v1=", StringComparison.Ordinal)).Select(x => x[3..])
            .Where(x => x.IndexOf(':') > 0).Select(x => x[(x.IndexOf(':') + 1)..])
            .Where(x => x.Length == 128 && x.All(Uri.IsHexDigit)).ToArray();
        if (timestampPart is null || signatures.Length == 0 || !long.TryParse(timestampPart.AsSpan(2), out var timestamp)) return false;
        DateTimeOffset signedAt;
        try { signedAt = DateTimeOffset.UnixEpoch.AddSeconds(timestamp); } catch { return false; }
        if ((clock.GetUtcNow() - signedAt).Duration() > TimeSpan.FromMinutes(5)) return false;
        var expected = HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{raw}"));
        return signatures.Any(s => CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(s)));
    }

    private static bool ValidLegacySignature(string raw, string signature, string secret)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(secret)) return false;
        var expected = Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return signature.Length == expected.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature.ToLowerInvariant()));
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

public sealed record CreatePaystackAccountRequest(string BankCode, string AccountNumber);
