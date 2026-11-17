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
        var organizationId = currentOrganization.OrganizationId;
        if (organizationId is null) return BadRequest(new { error = "No active business workspace." });
        var organization = await db.Organizations.AsNoTracking()
            .Where(x => x.Id == organizationId.Value)
            .Select(x => new { x.BaseCurrencyCode, x.CountryCode })
            .SingleOrDefaultAsync(ct);
        if (organization is null) return NotFound();
        var accounts = await db.OrganizationPaystackAccounts.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId.Value)
            .OrderBy(x => x.CurrencyCode)
            .ToListAsync(ct);
        var primary = accounts.FirstOrDefault(x => x.CurrencyCode == organization.BaseCurrencyCode) ?? accounts.FirstOrDefault();
        return Ok(new
        {
            status = accounts.Count == 0 || accounts.All(x => !x.IsActive) ? "Setup needed" : accounts.Any(CanCollect) ? "Ready" : "Pending verification",
            organization.BaseCurrencyCode, organization.CountryCode,
            defaultPaymentCurrencyCode = DefaultPaymentCurrency(organization.CountryCode, organization.BaseCurrencyCode),
            currencyCode = primary?.CurrencyCode,
            isActive = primary?.IsActive ?? false,
            isVerified = primary?.IsVerified ?? false,
            isTestMode = TestMode,
            accounts = accounts.Select(account => new
            {
                status = CanCollect(account) ? TestMode && !account.IsVerified ? "Test mode ready" : "Ready" : "Pending verification",
                account.CurrencyCode, account.IsActive, account.IsVerified,
                isTestMode = TestMode, account.LastCheckedAt
            })
        });
    }

    [HttpDelete("api/payment-accounts/paystack/{currencyCode}")]
    [Authorize(Policy = nameof(Permission.ManageOrganization))]
    public async Task<IActionResult> RemoveAccountLink(string currencyCode, CancellationToken ct)
    {
        var organizationId = currentOrganization.OrganizationId;
        if (organizationId is null) return BadRequest(new { error = "No active business workspace." });
        var currency = currencyCode.Trim().ToUpperInvariant();
        if (currency.Length != 3) return BadRequest(new { error = "Enter a valid account currency." });

        var account = await db.OrganizationPaystackAccounts.SingleOrDefaultAsync(
            x => x.OrganizationId == organizationId.Value && x.CurrencyCode == currency, ct);
        if (account is null) return NotFound(new { error = "That payment account is no longer connected." });
        if (account.IsActive)
            return Conflict(new { error = "This payment account is active. Deactivate or delete it in Paystack before removing its Invoice AI link." });

        db.OrganizationPaystackAccounts.Remove(account);
        await db.SaveChangesAsync(ct);
        return await Account(ct);
    }

    [HttpGet("api/payment-accounts/paystack/banks")]
    [Authorize(Policy = nameof(Permission.ManageOrganization))]
    public async Task<IActionResult> Banks(CancellationToken ct)
    {
        if (!paystack.Enabled || string.IsNullOrWhiteSpace(paystack.SecretKey))
            return Problem("Online payments are not configured yet.", statusCode: 503);

        var organizationId = currentOrganization.OrganizationId;
        if (organizationId is null) return BadRequest(new { error = "No active business workspace." });
        var countryCode = await db.Organizations.AsNoTracking()
            .Where(x => x.Id == organizationId.Value)
            .Select(x => x.CountryCode)
            .SingleOrDefaultAsync(ct);
        var country = countryCode?.ToUpperInvariant() switch
        {
            "NG" => "nigeria",
            "GH" => "ghana",
            "KE" => "kenya",
            "ZA" => "south africa",
            _ => null
        };
        if (country is null)
            return BadRequest(new { error = "Online bank setup is not available for this business country yet." });

        using var client = clients.CreateClient("Paystack");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"bank?country={Uri.EscapeDataString(country)}&perPage=100");
        request.Headers.Authorization = new("Bearer", paystack.SecretKey);
        try
        {
            using var response = await client.SendAsync(request, ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!response.IsSuccessStatusCode || !json.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
            {
                logger.LogWarning("Paystack bank list failed with HTTP {StatusCode}.", response.StatusCode);
                return Problem("The bank list is temporarily unavailable. Please try again.", statusCode: 502);
            }

            var banks = data.EnumerateArray()
                .Where(bank => TryString(bank, "name", out _) && TryString(bank, "code", out _)
                    && (!bank.TryGetProperty("active", out var active) || active.ValueKind == JsonValueKind.True)
                    && (!bank.TryGetProperty("is_deleted", out var deleted) || deleted.ValueKind != JsonValueKind.True))
                .Select(bank => new { name = bank.GetProperty("name").GetString()!, code = bank.GetProperty("code").GetString()! })
                .OrderBy(bank => bank.name)
                .ToArray();

            return Ok(new { banks });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not load Paystack bank list.");
            return Problem("The bank list is temporarily unavailable. Please try again.", statusCode: 502);
        }
    }

    [HttpPost("api/payment-accounts/paystack")]
    [Authorize(Policy = nameof(Permission.ManageOrganization))]
    public async Task<IActionResult> CreateAccount(CreatePaystackAccountRequest input, CancellationToken ct)
    {
        if (!paystack.Enabled || string.IsNullOrWhiteSpace(paystack.SecretKey))
            return Problem("Online payments are not configured yet.", statusCode: 503);
        var organizationId = currentOrganization.OrganizationId;
        if (organizationId is null) return BadRequest(new { error = "No active business workspace." });
        if (input.AccountNumber.Length is < 8 or > 20 || input.BankCode.Length is < 2 or > 20
            || paystack.PlatformPercentageCharge is < 0 or > 100)
            return BadRequest(new { error = "Enter valid bank details." });

        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == organizationId, ct);
        if (organization is null) return NotFound();
        var defaultPaymentCurrency = DefaultPaymentCurrency(organization.CountryCode, organization.BaseCurrencyCode);
        if (await db.OrganizationPaystackAccounts.AnyAsync(x => x.OrganizationId == organizationId.Value
                && x.CurrencyCode == defaultPaymentCurrency, ct))
            return Conflict(new { error = $"A {defaultPaymentCurrency} Paystack account is already connected." });
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
            if (await db.OrganizationPaystackAccounts.AnyAsync(x => x.OrganizationId == organizationId.Value && x.CurrencyCode == currency, ct))
                return Conflict(new { error = $"A Paystack account for {currency} is already connected." });
            var account = OrganizationPaystackAccount.Create(organizationId.Value, code, currency);
            account.RefreshProviderStatus(
                data.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True,
                data.TryGetProperty("is_verified", out var verified) && verified.ValueKind == JsonValueKind.True,
                clock.GetUtcNow());
            db.OrganizationPaystackAccounts.Add(account);
            await db.SaveChangesAsync(ct);
            return await Account(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not reach Paystack while creating a subaccount.");
            return Problem("Could not reach Paystack. Please try again later.", statusCode: 502);
        }
    }

    [HttpPost("api/payment-accounts/paystack/link")]
    [Authorize(Policy = nameof(Permission.ManageOrganization))]
    public async Task<IActionResult> LinkUsdAccount(LinkPaystackUsdAccountRequest input, CancellationToken ct)
    {
        if (!paystack.Enabled || string.IsNullOrWhiteSpace(paystack.SecretKey))
            return Problem("Online payments are not configured yet.", statusCode: 503);
        var organizationId = currentOrganization.OrganizationId;
        if (organizationId is null) return BadRequest(new { error = "No active business workspace." });
        var countryCode = await db.Organizations.AsNoTracking().Where(x => x.Id == organizationId.Value)
            .Select(x => x.CountryCode).SingleOrDefaultAsync(ct);
        if (countryCode?.ToUpperInvariant() is not ("NG" or "KE"))
            return BadRequest(new { error = "Paystack USD accounts are only available to eligible businesses in Nigeria or Kenya." });
        if (string.IsNullOrWhiteSpace(input.SubaccountCode) || input.SubaccountCode.Length > 100)
            return BadRequest(new { error = "Enter the USD subaccount code from your Paystack dashboard." });
        if (await db.OrganizationPaystackAccounts.AnyAsync(x => x.OrganizationId == organizationId.Value && x.CurrencyCode == "USD", ct))
            return Conflict(new { error = "A USD Paystack account is already connected." });

        using var client = clients.CreateClient("Paystack");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"subaccount/{Uri.EscapeDataString(input.SubaccountCode.Trim())}");
        request.Headers.Authorization = new("Bearer", paystack.SecretKey);
        try
        {
            using var response = await client.SendAsync(request, ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!response.IsSuccessStatusCode || !json.RootElement.TryGetProperty("data", out var data)
                || !TryString(data, "subaccount_code", out var code))
                return BadRequest(new { error = "Paystack could not find that subaccount. Check the code and make sure it belongs to this Paystack business." });
            if (!TryString(data, "currency", out var currency) || !string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = "That Paystack subaccount is not set up in USD. Create a USD subaccount in Paystack and try again." });
            var active = data.TryGetProperty("active", out var activeValue) && activeValue.ValueKind == JsonValueKind.True;
            var verified = data.TryGetProperty("is_verified", out var verifiedValue) && verifiedValue.ValueKind == JsonValueKind.True;
            if (!active) return BadRequest(new { error = "That Paystack USD subaccount is inactive. Activate it in Paystack before connecting it." });

            var account = OrganizationPaystackAccount.Create(organizationId.Value, code, "USD");
            account.RefreshProviderStatus(active, verified, clock.GetUtcNow());
            db.OrganizationPaystackAccounts.Add(account);
            await db.SaveChangesAsync(ct);
            return await Account(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not verify a USD Paystack subaccount.");
            return Problem("Could not verify the USD account with Paystack. Please try again later.", statusCode: 502);
        }
    }

    [HttpPost("api/payment-accounts/paystack/refresh")]
    [Authorize(Policy = nameof(Permission.ManageOrganization))]
    public async Task<IActionResult> RefreshAccount(CancellationToken ct)
    {
        if (!paystack.Enabled || string.IsNullOrWhiteSpace(paystack.SecretKey)) return Problem("Online payments are not configured yet.", statusCode: 503);
        var accounts = await db.OrganizationPaystackAccounts.ToListAsync(ct);
        if (accounts.Count == 0) return NotFound(new { error = "Set up a Paystack account first." });
        using var client = clients.CreateClient("Paystack");
        foreach (var account in accounts)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"subaccount/{Uri.EscapeDataString(account.SubaccountCode)}");
            request.Headers.Authorization = new("Bearer", paystack.SecretKey);
            using var response = await client.SendAsync(request, ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!response.IsSuccessStatusCode || !json.RootElement.TryGetProperty("data", out var data))
                return Problem("Paystack could not confirm one of your connected accounts yet.", statusCode: 502);
            var currency = TryString(data, "currency", out var code) ? code : account.CurrencyCode;
            var active = data.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.True;
            var verified = data.TryGetProperty("is_verified", out var v) && v.ValueKind == JsonValueKind.True;
            account.RefreshProviderStatus(active && string.Equals(currency, account.CurrencyCode, StringComparison.OrdinalIgnoreCase), verified, clock.GetUtcNow());
        }
        await db.SaveChangesAsync(ct);
        return await Account(ct);
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
        var accounts = await db.OrganizationPaystackAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.OrganizationId == invoice.OrganizationId).ToListAsync(ct);
        var account = accounts.SingleOrDefault(a => a.CurrencyCode == invoice.CurrencyCode);
        var paymentUnavailableMessage = !paystack.Enabled || string.IsNullOrWhiteSpace(paystack.SecretKey)
            ? "Online payments are temporarily unavailable. Please contact the business to arrange payment."
            : account is null && accounts.Any(CanCollect)
                ? $"This invoice is in {invoice.CurrencyCode}, but online payments are ready for {string.Join(", ", accounts.Where(CanCollect).Select(x => x.CurrencyCode).Distinct().Order())}. Ask the business to connect a {invoice.CurrencyCode} Paystack account or send an invoice in a connected currency."
            : account is null || !CanCollect(account)
                ? "This business is still setting up online payments. Please contact the business to arrange payment another way."
            : string.IsNullOrWhiteSpace(customer.Email)
                ? "Online payment is not available for this invoice yet. Please contact the business to arrange payment."
            : null;
        var invoiceCanBePaid = invoice.Balance > 0
            && invoice.Status is InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid;
        return Ok(new { invoice.Number, businessName = organization.Name, customerName = customer.Name,
            invoice.CurrencyCode, invoice.Total, invoice.AmountPaid, invoice.Balance, invoice.DueDate,
            status = invoice.Status.ToString(), canPay = invoiceCanBePaid && paymentUnavailableMessage is null,
            availablePaymentCurrencies = accounts.Where(CanCollect).Select(x => x.CurrencyCode).Distinct().Order().ToArray(),
            paymentUnavailableMessage = invoiceCanBePaid ? paymentUnavailableMessage : null });
    }

    [HttpPost("api/public/invoices/{token}/confirm")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ConfirmPublicInvoicePayment(string token, [FromQuery] string reference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 100) return BadRequest();
        var invoice = await db.Invoices.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(i => i.PublicPaymentToken == token, ct);
        if (invoice is null || invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Void) return NotFound();
        var checkoutExists = await db.InvoicePaymentCheckouts.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(c => c.Reference == reference && c.InvoiceId == invoice.Id
                           && c.OrganizationId == invoice.OrganizationId, ct);
        if (!checkoutExists) return NotFound();

        // The browser return is not proof of payment. Ask Paystack to verify this exact checkout
        // server-side so the invoice can update even when its webhook arrives late or is misconfigured.
        await invoicePaymentWebhook.ProcessSuccessfulReference(reference, ct);

        invoice = await db.Invoices.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == invoice.Id && i.OrganizationId == invoice.OrganizationId, ct);
        if (invoice is null) return NotFound();
        return Ok(new { invoice.Number, businessName = (await db.Organizations.AsNoTracking()
                .Where(x => x.Id == invoice.OrganizationId).Select(x => x.Name).SingleAsync(ct)),
            customerName = await db.Customers.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.Id == invoice.CustomerId && x.OrganizationId == invoice.OrganizationId)
                .Select(x => x.Name).SingleAsync(ct),
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
            .SingleOrDefaultAsync(a => a.OrganizationId == invoice.OrganizationId && a.CurrencyCode == invoice.CurrencyCode, ct);
        if (account is null || !CanCollect(account))
            return Conflict(new { error = "This business is still completing payment account verification." });
        var customer = await db.Customers.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == invoice.CustomerId && c.OrganizationId == invoice.OrganizationId, ct);
        if (customer?.Email is null) return Conflict(new { error = "The business needs to add an email address for this customer before accepting online payment." });

        var previous = await db.InvoicePaymentCheckouts.IgnoreQueryFilters()
            .Where(c => c.InvoiceId == invoice.Id && c.Status == InvoicePaymentCheckoutStatus.Pending)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
        var pendingWindow = previous is not null && string.IsNullOrWhiteSpace(previous.AuthorizationUrl)
            ? TimeSpan.FromMinutes(2)
            : TimeSpan.FromMinutes(30);
        if (previous is not null && previous.CreatedAt > clock.GetUtcNow().Subtract(pendingWindow))
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
            callback_url = $"{links.FrontendBaseUrl.TrimEnd('/')}/?pay={Uri.EscapeDataString(token)}",
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
        if (!payload.RootElement.TryGetProperty("event", out var eventName)
            || eventName.ValueKind != JsonValueKind.String
            || !payload.RootElement.TryGetProperty("data", out var data)) return Ok();
        var eventValue = eventName.GetString()!;
        if (eventValue == "charge.success")
        {
            if (!TryString(data, "reference", out var reference) || !reference.StartsWith("invpay_", StringComparison.Ordinal)) return Ok();
            await invoicePaymentWebhook.ProcessSuccessfulCharge(data, ct);
        }
        else if (eventValue.StartsWith("refund.", StringComparison.Ordinal))
            await invoicePaymentWebhook.ProcessRefundUpdate(data, eventValue, ct);
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

    private bool TestMode => paystack.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal);
    private bool CanCollect(OrganizationPaystackAccount account) => account.IsActive && (account.IsVerified || TestMode);
    private static string DefaultPaymentCurrency(string? countryCode, string fallback) => countryCode?.ToUpperInvariant() switch
    {
        "NG" => "NGN",
        "GH" => "GHS",
        "KE" => "KES",
        "ZA" => "ZAR",
        _ => fallback
    };

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
public sealed record LinkPaystackUsdAccountRequest(string SubaccountCode);
