using System.Globalization;
using System.Text.Json;
using Application.Common.Interfaces;
using Application.Invoices;
using Domain.Billing;
using Domain.Invoices;
using Domain.Organizations;
using Domain.Payments;
using Host;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Host.Controllers;

[ApiController]
[Route("api/invoices/{invoiceId:guid}/credit-notes/{creditNoteId:guid}/refunds")]
public sealed class CreditNoteRefundsController(
    AppDbContext db, ICurrentOrganization currentOrganization, IHttpClientFactory clients,
    PaystackSettings paystack, TimeProvider clock, ILogger<CreditNoteRefundsController> logger) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<IReadOnlyList<CreditNoteRefundDto>>> List(Guid invoiceId, Guid creditNoteId, CancellationToken ct)
    {
        var note = await db.CreditNotes.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == creditNoteId && x.InvoiceId == invoiceId, ct);
        if (note is null) return NotFound();
        var rows = await db.CreditNoteRefunds.AsNoTracking().Where(x => x.CreditNoteId == creditNoteId)
            .OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        var refunds = rows.Select(ToDto).ToList();
        return Ok(refunds);
    }

    [HttpPost]
    [Authorize(Policy = nameof(Permission.IssueInvoices))]
    public async Task<ActionResult<CreditNoteRefundDto>> Create(
        Guid invoiceId, Guid creditNoteId, CreditNoteRefundCommand command, CancellationToken ct)
    {
        if (command.Amount <= 0 || decimal.Round(command.Amount, 2) != command.Amount)
            return BadRequest(new { error = "Enter a refund amount with up to two decimal places." });

        var organizationId = currentOrganization.OrganizationId;
        if (organizationId is null) return Unauthorized();
        CreditNoteRefund? refund = null;

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var invoiceVisible = await db.Invoices.AsNoTracking()
                .AnyAsync(x => x.Id == invoiceId, ct);
            if (!invoiceVisible) return NotFound();
            await db.Database.ExecuteSqlAsync(
                $"SELECT 1 FROM \"Invoices\" WHERE \"Id\" = {invoiceId} FOR UPDATE", ct);

            var note = await db.CreditNotes.SingleOrDefaultAsync(
                x => x.Id == creditNoteId && x.InvoiceId == invoiceId, ct);
            var payment = await db.Payments.SingleOrDefaultAsync(
                x => x.Id == command.PaymentId && x.InvoiceId == invoiceId, ct);
            if (note is null || payment is null) return NotFound();
            if (payment.IsReversed) return Conflict(new { error = "A reversed payment cannot be refunded." });
            if (note.CurrencyCode != payment.CurrencyCode)
                return Conflict(new { error = "The payment and credit note use different currencies." });

            var heldNoteAmount = await db.CreditNoteRefunds
                .Where(x => x.CreditNoteId == creditNoteId && x.Status != CreditNoteRefundStatus.Failed)
                .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
            var heldPaymentAmount = await db.CreditNoteRefunds
                .Where(x => x.PaymentId == payment.Id && x.Status != CreditNoteRefundStatus.Failed)
                .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
            if (command.Amount > note.Amount - heldNoteAmount)
                return Conflict(new { error = "This refund is more than the credit note's remaining refundable amount." });
            if (command.Amount > payment.Amount - heldPaymentAmount)
                return Conflict(new { error = "This payment does not have enough refundable funds remaining." });

            string? providerReference = null;
            if (command.Channel == CreditNoteRefundChannel.Paystack)
            {
                if (payment.Method != PaymentMethod.Card || payment.Reference is null
                    || !payment.Reference.StartsWith("invpay_", StringComparison.Ordinal))
                    return Conflict(new { error = "Only an online card payment made through Paystack can be refunded here." });
                var verifiedCheckout = await db.InvoicePaymentCheckouts.AsNoTracking().AnyAsync(
                    x => x.InvoiceId == invoiceId && x.Reference == payment.Reference
                         && x.Status == InvoicePaymentCheckoutStatus.Succeeded, ct);
                if (!verifiedCheckout)
                    return Conflict(new { error = "This payment could not be verified as a completed Paystack transaction." });
                if (!paystack.Enabled || string.IsNullOrWhiteSpace(paystack.SecretKey))
                    return Problem("Paystack refunds are not configured.", statusCode: 503);
                providerReference = $"invrf_{Guid.CreateVersion7():N}";
            }

            refund = CreditNoteRefund.Create(organizationId.Value, invoiceId, creditNoteId, payment.Id,
                payment.CurrencyCode, command.Amount, command.Channel, command.Reason,
                command.Channel == CreditNoteRefundChannel.External
                    ? string.IsNullOrWhiteSpace(command.Reference) ? null : command.Reference.Trim()
                    : providerReference,
                clock.GetUtcNow());
            db.CreditNoteRefunds.Add(refund);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            if (command.Channel == CreditNoteRefundChannel.External)
                return CreatedAtAction(nameof(List), new { invoiceId, creditNoteId }, ToDto(refund));
        }

        try
        {
            var payment = await db.Payments.AsNoTracking().SingleAsync(x => x.Id == command.PaymentId, ct);
            var minorAmount = decimal.ToInt64(decimal.Round(command.Amount * 100m, 0, MidpointRounding.AwayFromZero));
            using var client = clients.CreateClient("Paystack");
            using var request = new HttpRequestMessage(HttpMethod.Post, "refund");
            request.Headers.Authorization = new("Bearer", paystack.SecretKey);
            request.Content = JsonContent.Create(new
            {
                transaction = payment.Reference,
                amount = minorAmount,
                currency = payment.CurrencyCode,
                merchant_note = command.Reason,
                reference = refund!.ProviderReference
            });
            using var response = await client.SendAsync(request, ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode < 500)
                {
                    refund!.ApplyProviderStatus("failed", "Paystack rejected the refund request.", clock.GetUtcNow());
                    await db.SaveChangesAsync(ct);
                }
                else logger.LogWarning("Paystack refund returned HTTP {StatusCode}; refund {RefundId} remains pending for reconciliation.", response.StatusCode, refund!.Id);
                return Problem("Paystack could not confirm the refund request. Check the refund status before trying again.", statusCode: 502);
            }

            if (json.RootElement.TryGetProperty("data", out var data)
                && TryString(data, "status", out var status))
            {
                refund!.ApplyProviderStatus(status, null, clock.GetUtcNow());
                await db.SaveChangesAsync(ct);
            }
            return Accepted(ToDto(refund!));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Paystack refund result is unknown for refund {RefundId}; the refund remains pending.", refund!.Id);
            return Problem("The refund request may have reached Paystack. Check its status before trying again.", statusCode: 502);
        }
    }

    private static CreditNoteRefundDto ToDto(CreditNoteRefund x)
        => new(x.Id, x.InvoiceId, x.CreditNoteId, x.PaymentId, x.CurrencyCode, x.Amount,
            x.Channel, x.Status, x.Reason, x.ProviderReference, x.CreatedAt, x.ProcessedAt, x.FailureReason);

    private static bool TryString(JsonElement value, string key, out string result)
    {
        result = "";
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var element)
               && element.ValueKind == JsonValueKind.String && (result = element.GetString() ?? "").Length > 0;
    }
}
