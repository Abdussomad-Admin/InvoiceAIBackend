using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Common;
using Application.Common.Interfaces;
using Domain.Billing;
using Domain.Organizations;
using Host;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Host.Controllers;

[ApiController]
public sealed class BillingController(
    AppDbContext db,
    ICurrentOrganization currentOrganization,
    ICurrentUser currentUser,
    IHttpClientFactory clients,
    BillingSettings billing,
    PaystackSettings paystack,
    AppLinkSettings links,
    TimeProvider clock,
    InvoicePaymentWebhookProcessor invoicePaymentWebhook,
    ILogger<BillingController> logger) : ControllerBase
{
    [HttpGet("api/billing/plans")]
    [AllowAnonymous]
    public ActionResult Plans()
        => Ok(billing.Plans.Where(IsValidPlan).Select(p => new
        {
            p.Code, p.Name, p.Description, p.CurrencyCode, p.AmountMinor, p.Interval, p.IsFree,
            p.InvoiceLimit, p.MemberLimit, p.AiExtractionLimit, p.StorageLimitBytes,
            canCheckout = !p.IsFree && paystack.Enabled && !string.IsNullOrWhiteSpace(p.PaystackPlanCode)
        }).ToArray());

    [HttpGet("api/billing/subscription")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<IActionResult> Subscription(CancellationToken ct)
    {
        var sub = await db.OrganizationSubscriptions.AsNoTracking().SingleOrDefaultAsync(ct);
        var plan = sub is null
            ? billing.Plans.FirstOrDefault(p => p.IsFree)
            : billing.Plans.FirstOrDefault(p => p.Code == sub.PlanCode);
        if (sub is null) return Ok(new { status = "Free", plan = plan is null ? null : new { plan.Code,
            plan.Name, plan.Description, plan.InvoiceLimit, plan.MemberLimit, plan.AiExtractionLimit,
            plan.StorageLimitBytes } });
        return Ok(new { sub.Id, sub.PlanCode, sub.Status, sub.CurrencyCode, sub.AmountMinor,
            sub.CurrentPeriodStart, sub.CurrentPeriodEnd, sub.LastPaidAt,
            plan = plan is null ? null : new { plan.Name, plan.Description, plan.InvoiceLimit,
                plan.MemberLimit, plan.AiExtractionLimit, plan.StorageLimitBytes } });
    }

    [HttpGet("api/billing/history")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<IActionResult> History(CancellationToken ct)
    {
        var subscription = await db.OrganizationSubscriptions.AsNoTracking().SingleOrDefaultAsync(ct);
        var checkouts = await db.BillingCheckouts.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .Select(x => new { x.Id, x.PlanCode, x.CurrencyCode, x.AmountMinor, x.Status, x.CreatedAt })
            .ToListAsync(ct);

        var plan = subscription is null
            ? billing.Plans.FirstOrDefault(p => p.IsFree)
            : billing.Plans.FirstOrDefault(p => p.Code == subscription.PlanCode);
        return Ok(new
        {
            subscription = subscription is null
                ? new { status = "Free", planCode = (string?)plan?.Code, planName = plan?.Name,
                    currencyCode = (string?)null, amountMinor = (long?)null,
                    currentPeriodStart = (DateTimeOffset?)null, currentPeriodEnd = (DateTimeOffset?)null,
                    canCancel = false }
                : new { status = subscription.Status.ToString(), planCode = (string?)subscription.PlanCode,
                    planName = plan?.Name, currencyCode = (string?)subscription.CurrencyCode,
                    amountMinor = (long?)subscription.AmountMinor, currentPeriodStart = subscription.CurrentPeriodStart,
                    currentPeriodEnd = subscription.CurrentPeriodEnd,
                    canCancel = subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue },
            checkouts
        });
    }

    [HttpPost("api/billing/checkout")]
    [Authorize(Policy = nameof(Permission.ManageOrganization))]
    public async Task<IActionResult> Checkout(CheckoutRequest request, CancellationToken ct)
    {
        if (!paystack.Enabled || string.IsNullOrWhiteSpace(paystack.SecretKey))
            return Problem("Subscription checkout is not configured.", statusCode: 503);
        var plan = billing.Plans.SingleOrDefault(p => p.Code == request.PlanCode && IsValidPlan(p)
            && !p.IsFree && !string.IsNullOrWhiteSpace(p.PaystackPlanCode));
        if (plan is null) return BadRequest(new { error = "Unknown or unavailable plan." });
        var organizationId = currentOrganization.OrganizationId;
        if (organizationId is null) return BadRequest(new { error = "No active organization." });
        var existing = await db.OrganizationSubscriptions.AsNoTracking().SingleOrDefaultAsync(ct);
        if (existing is not null && existing.Status != SubscriptionStatus.Canceled)
        {
            if (existing.PlanCode == plan.Code)
                return Conflict(new { error = "This organization is already subscribed to that plan." });
            if (existing.AmountMinor > 0 && string.IsNullOrWhiteSpace(existing.PaystackSubscriptionCode)
                && existing.Status is not (SubscriptionStatus.Canceled or SubscriptionStatus.NonRenewing))
                return Conflict(new { error = "The existing subscription is missing its provider reference; contact support before changing plans." });
        }
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == currentUser.UserId, ct);
        if (user is null || !user.IsEmailVerified) return BadRequest(new { error = "A verified account email is required for billing." });

        var reference = $"invai_{Guid.CreateVersion7():N}";
        var checkout = BillingCheckout.Create(organizationId.Value, plan.Code,
            plan.CurrencyCode.ToUpperInvariant(), plan.AmountMinor, reference);
        db.BillingCheckouts.Add(checkout);
        await db.SaveChangesAsync(ct);

        using var client = clients.CreateClient("Paystack");
        using var message = new HttpRequestMessage(HttpMethod.Post, "transaction/initialize");
        message.Headers.Authorization = new("Bearer", paystack.SecretKey);
        message.Content = JsonContent.Create(new
        {
            email = user.Email,
            amount = plan.AmountMinor,
            currency = plan.CurrencyCode.ToUpperInvariant(),
            plan = plan.PaystackPlanCode,
            reference,
            callback_url = $"{links.FrontendBaseUrl.TrimEnd('/')}/app/billing/return",
            metadata = new { organizationId = organizationId.Value, planCode = plan.Code,
                purpose = existing is null || existing.Status == SubscriptionStatus.Canceled ? "subscription" : "plan_change" }
        });
        try
        {
            using var response = await client.SendAsync(message, ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!response.IsSuccessStatusCode || !json.RootElement.TryGetProperty("data", out var data)
                || !data.TryGetProperty("authorization_url", out var url))
            {
                checkout.MarkFailed();
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Paystack checkout initialization failed: {StatusCode}", response.StatusCode);
                return Problem("Could not initialize subscription checkout.", statusCode: 502);
            }
            checkout.SetAuthorizationUrl(url.GetString() ?? "");
            await db.SaveChangesAsync(ct);
            return Ok(new { reference, authorizationUrl = checkout.AuthorizationUrl });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            checkout.MarkFailed();
            await db.SaveChangesAsync(ct);
            logger.LogWarning(ex, "Paystack checkout request failed.");
            return Problem("Could not reach the payment provider.", statusCode: 502);
        }
    }

    [HttpPost("api/billing/checkout/confirm")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<IActionResult> ConfirmCheckout([FromQuery] string reference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return BadRequest(new { error = "The payment reference is missing." });
        var organizationId = currentOrganization.OrganizationId;
        if (organizationId is null) return BadRequest(new { error = "No active workspace." });

        var checkout = await db.BillingCheckouts.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Reference == reference && x.OrganizationId == organizationId, ct);
        if (checkout is null) return NotFound(new { error = "This payment could not be found in your workspace." });

        // Treat the browser redirect only as a signal to check. The provider's verified
        // transaction response is still required before the checkout/subscription is saved.
        if (checkout.Status == BillingCheckoutStatus.Pending)
        {
            using var callback = JsonDocument.Parse(JsonSerializer.Serialize(new { reference }));
            await ProcessSuccessfulCharge(callback.RootElement, clock.GetUtcNow(), ct);
        }

        var updated = await db.BillingCheckouts.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.Reference == reference && x.OrganizationId == organizationId, ct);
        var subscription = await db.OrganizationSubscriptions.AsNoTracking().SingleOrDefaultAsync(ct);
        return Ok(new
        {
            status = updated.Status.ToString(),
            updated.PlanCode,
            updated.CurrencyCode,
            updated.AmountMinor,
            currentPlanCode = subscription?.PlanCode,
            subscriptionStatus = subscription?.Status.ToString()
        });
    }

    [HttpPost("api/billing/subscription/cancel")]
    [Authorize(Policy = nameof(Permission.ManageOrganization))]
    public async Task<IActionResult> CancelSubscription(CancellationToken ct)
    {
        if (!paystack.Enabled || string.IsNullOrWhiteSpace(paystack.SecretKey))
            return Problem("Subscription management is not configured.", statusCode: 503);
        var sub = await db.OrganizationSubscriptions.SingleOrDefaultAsync(ct);
        if (sub is null) return NotFound(new { error = "No subscription exists for this organization." });
        if (sub.Status is SubscriptionStatus.Canceled or SubscriptionStatus.NonRenewing)
            return Ok(new { status = sub.Status, sub.CurrentPeriodEnd });
        if (string.IsNullOrWhiteSpace(sub.PaystackSubscriptionCode))
            return Conflict(new { error = "The provider subscription reference is missing; contact support." });

        var cancellation = await DisableProviderSubscription(sub.PaystackSubscriptionCode, ct);
        if (!cancellation.Success)
        {
            logger.LogWarning("Paystack could not cancel subscription {SubscriptionCode}: {Reason}",
                sub.PaystackSubscriptionCode, cancellation.Error);
            return Problem("Paystack could not cancel this subscription. Please try again.", statusCode: 502);
        }
        sub.SetStatus(SubscriptionStatus.NonRenewing);
        await db.SaveChangesAsync(ct);
        return Ok(new { status = sub.Status, sub.CurrentPeriodEnd });
    }

    [HttpPost("api/billing/paystack/webhook")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PaystackWebhook(CancellationToken ct)
    {
        if (!paystack.Enabled || string.IsNullOrWhiteSpace(paystack.SecretKey)) return NotFound();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync(ct);
        var validSignature = Request.Headers.TryGetValue("Paystack-Signature", out var currentSignature)
            ? ValidCurrentSignature(raw, currentSignature.ToString(), paystack.WebhookSecret)
            : Request.Headers.TryGetValue("x-paystack-signature", out var legacySignature)
              && ValidSignature(raw, legacySignature.ToString(), paystack.SecretKey);
        if (!validSignature)
            return Unauthorized();
        JsonDocument payload;
        try { payload = JsonDocument.Parse(raw); }
        catch (JsonException) { return BadRequest(); }
        using (payload)
        {
            var root = payload.RootElement;
            var eventName = root.TryGetProperty("event", out var ev) ? ev.GetString() : null;
            if (!root.TryGetProperty("data", out var data) || eventName is null) return Ok();
            var now = clock.GetUtcNow();
            if (eventName == "charge.success")
            {
                if (TryString(data, "reference", out var reference)
                    && reference.StartsWith("invpay_", StringComparison.Ordinal))
                    await invoicePaymentWebhook.ProcessSuccessfulCharge(data, ct);
                else
                    await ProcessSuccessfulCharge(data, now, ct);
            }
            else if (eventName is "invoice.payment_failed" or "invoice.update")
                await ProcessInvoiceEvent(eventName, data, now, ct);
            else if (eventName is "subscription.not_renew" or "subscription.disable" or "subscription.create")
                await ProcessSubscriptionEvent(eventName, data, ct);
        }
        return Ok();
    }

    private async Task ProcessSuccessfulCharge(JsonElement data, DateTimeOffset now, CancellationToken ct)
    {
        if (!TryString(data, "reference", out var reference)) return;
        var checkout = await db.BillingCheckouts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Reference == reference, ct);
        if (checkout is null || checkout.Status == BillingCheckoutStatus.Succeeded) return;
        using var client = clients.CreateClient("Paystack");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"transaction/verify/{Uri.EscapeDataString(reference)}");
        request.Headers.Authorization = new("Bearer", paystack.SecretKey);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return;
        using var verifiedDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!verifiedDoc.RootElement.TryGetProperty("data", out var verified)
            || !TryString(verified, "status", out var status) || status != "success"
            || !TryLong(verified, "amount", out var amount) || amount != checkout.AmountMinor
            || !TryString(verified, "currency", out var currency)
            || !string.Equals(currency, checkout.CurrencyCode, StringComparison.OrdinalIgnoreCase)) return;

        var subscriptionCode = TryNestedString(data, "subscription", "subscription_code");
        if (subscriptionCode is null && TryString(data, "subscription_code", out var directSubscriptionCode))
            subscriptionCode = directSubscriptionCode;
        subscriptionCode ??= TryNestedString(verified, "subscription", "subscription_code");
        if (subscriptionCode is null && TryString(verified, "subscription_code", out var verifiedSubscriptionCode))
            subscriptionCode = verifiedSubscriptionCode;
        var customerCode = TryNestedString(data, "customer", "customer_code")
                           ?? TryNestedString(verified, "customer", "customer_code");
        var plan = billing.Plans.FirstOrDefault(p => p.Code == checkout.PlanCode);
        var periodEnd = TryNestedDate(data, "subscription", "next_payment_date")
                        ?? PeriodEnd(now, plan?.Interval);
        var existingSub = await db.OrganizationSubscriptions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.OrganizationId == checkout.OrganizationId, ct);
        if (existingSub is not null && existingSub.Status != SubscriptionStatus.Canceled
            && !string.IsNullOrWhiteSpace(existingSub.PaystackSubscriptionCode)
            && existingSub.PaystackSubscriptionCode != subscriptionCode)
        {
            if (string.IsNullOrWhiteSpace(subscriptionCode))
                throw new InvalidOperationException("The new paid subscription could not be identified. Paystack webhook delivery will retry this operation.");
            var cancellation = await DisableProviderSubscription(existingSub.PaystackSubscriptionCode, ct);
            if (!cancellation.Success)
                throw new InvalidOperationException("The new subscription payment succeeded but the previous recurring subscription could not be canceled. Paystack webhook delivery will retry this operation.");
        }
        using (db.Allow())
        {
            checkout.MarkSucceeded();
            var sub = existingSub;
            if (sub is null)
            {
                sub = OrganizationSubscription.Start(checkout.OrganizationId, checkout.PlanCode,
                    checkout.CurrencyCode, checkout.AmountMinor);
                db.OrganizationSubscriptions.Add(sub);
            }
            else
                // A plan change is committed only after the new charge is verified.
                sub.ChangePlan(checkout.PlanCode, checkout.CurrencyCode, checkout.AmountMinor);
            sub.Activate(customerCode, subscriptionCode, now, periodEnd);
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<(bool Success, string? Error)> DisableProviderSubscription(string subscriptionCode, CancellationToken ct)
    {
        using var client = clients.CreateClient("Paystack");
        using var get = new HttpRequestMessage(HttpMethod.Get, $"subscription/{Uri.EscapeDataString(subscriptionCode)}");
        get.Headers.Authorization = new("Bearer", paystack.SecretKey);
        using var getResponse = await client.SendAsync(get, ct);
        if (!getResponse.IsSuccessStatusCode) return (false, $"subscription lookup returned {(int)getResponse.StatusCode}");
        using var getJson = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync(ct));
        if (!getJson.RootElement.TryGetProperty("data", out var data)) return (false, "provider response omitted subscription details");
        if (TryString(data, "status", out var status) && status != "active") return (true, null);
        if (!TryString(data, "email_token", out var token)) return (false, "provider response omitted the cancellation token");

        using var disable = new HttpRequestMessage(HttpMethod.Post, "subscription/disable");
        disable.Headers.Authorization = new("Bearer", paystack.SecretKey);
        disable.Content = JsonContent.Create(new { code = subscriptionCode, token });
        using var disableResponse = await client.SendAsync(disable, ct);
        if (!disableResponse.IsSuccessStatusCode) return (false, $"provider cancellation returned {(int)disableResponse.StatusCode}");
        using var disableJson = JsonDocument.Parse(await disableResponse.Content.ReadAsStringAsync(ct));
        return disableJson.RootElement.TryGetProperty("status", out var ok) && ok.ValueKind == JsonValueKind.True
            ? (true, null) : (false, "provider rejected the cancellation");
    }

    private async Task ProcessInvoiceEvent(string eventName, JsonElement data, DateTimeOffset now, CancellationToken ct)
    {
        var code = TryNestedString(data, "subscription", "subscription_code");
        if (code is null) return;
        var sub = await db.OrganizationSubscriptions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.PaystackSubscriptionCode == code, ct);
        if (sub is null) return;
        if (eventName == "invoice.payment_failed") sub.SetStatus(SubscriptionStatus.PastDue);
        else if (TryString(data, "status", out var status) && status == "success") sub.Activate(null, code, now);
        else sub.SetStatus(SubscriptionStatus.PastDue);
        await db.SaveChangesAsync(ct);
    }

    private async Task ProcessSubscriptionEvent(string eventName, JsonElement data, CancellationToken ct)
    {
        var code = TryString(data, "subscription_code", out var c) ? c : null;
        if (code is null) return;
        var sub = await db.OrganizationSubscriptions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.PaystackSubscriptionCode == code, ct);
        if (sub is null) return;
        if (eventName == "subscription.not_renew") sub.SetStatus(SubscriptionStatus.NonRenewing);
        if (eventName == "subscription.disable") sub.SetStatus(SubscriptionStatus.Canceled);
        if (eventName == "subscription.create")
            sub.SetProviderDetails(TryNestedString(data, "customer", "customer_code"), code);
        await db.SaveChangesAsync(ct);
    }

    private static bool IsValidPlan(BillingPlanSettings p)
        => !string.IsNullOrWhiteSpace(p.Code) && !string.IsNullOrWhiteSpace(p.Name)
           && p.CurrencyCode.Length == 3
           && (p.IsFree ? p.AmountMinor == 0 : p.AmountMinor > 0);

    private static bool ValidSignature(string raw, string signature, string secret)
    {
        try
        {
            var expected = Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
            return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature.ToLowerInvariant()));
        }
        catch { return false; }
    }

    private bool ValidCurrentSignature(string raw, string header, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return false;
        // Current Paystack signatures carry a timestamp and key id. Reject stale/replayed payloads.
        var parts = header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var timestampParts = parts.Where(p => p.StartsWith("t=", StringComparison.Ordinal)).ToArray();
        var signatures = parts.Where(p => p.StartsWith("v1=", StringComparison.Ordinal))
            .Select(p => p[3..]).Where(p => p.IndexOf(':') > 0)
            .Select(p => p[(p.IndexOf(':') + 1)..])
            .Where(p => p.Length == 128 && p.All(Uri.IsHexDigit)).ToArray();
        if (timestampParts.Length != 1 || signatures.Length == 0
            || !long.TryParse(timestampParts[0].AsSpan(2), out var timestamp)) return false;
        DateTimeOffset signedAt;
        try { signedAt = DateTimeOffset.UnixEpoch.AddSeconds(timestamp); }
        catch (ArgumentOutOfRangeException) { return false; }
        if ((clock.GetUtcNow() - signedAt).Duration() > TimeSpan.FromMinutes(5)) return false;
        var expected = Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{raw}"))).ToLowerInvariant();
        var expectedBytes = Convert.FromHexString(expected);
        return signatures.Any(signature => CryptographicOperations.FixedTimeEquals(
            expectedBytes, Convert.FromHexString(signature)));
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
    private static string? TryNestedString(JsonElement value, string outer, string inner)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(outer, out var nested)
           && TryString(nested, inner, out var result) ? result : null;
    private static DateTimeOffset? TryNestedDate(JsonElement value, string outer, string inner)
        => TryNestedString(value, outer, inner) is { } text && DateTimeOffset.TryParse(text, out var date)
            ? date : null;
    private static DateTimeOffset? PeriodEnd(DateTimeOffset now, string? interval)
        => interval?.ToLowerInvariant() switch
        {
            "daily" => now.AddDays(1),
            "weekly" => now.AddDays(7),
            "monthly" => now.AddMonths(1),
            "quarterly" => now.AddMonths(3),
            "biannually" => now.AddMonths(6),
            "annually" => now.AddYears(1),
            _ => null
        };
}

public sealed record CheckoutRequest(string PlanCode);
