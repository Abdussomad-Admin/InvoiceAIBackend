using Domain.Common;
using Domain.Payments;

namespace Domain.Invoices;

public enum CreditNoteRefundChannel { External = 0, Paystack = 1 }
public enum CreditNoteRefundStatus { Pending = 0, Processing = 1, NeedsAttention = 2, Processed = 3, Failed = 4 }

// Refunds are recorded separately from incoming payments so invoice balances retain the credit note,
// while the cash outflow remains visible for reconciliation and reporting.
public sealed class CreditNoteRefund : BaseEntity, ITenantOwned
{
    private CreditNoteRefund() { }

    public Guid OrganizationId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid CreditNoteId { get; private set; }
    public Guid PaymentId { get; private set; }
    public string CurrencyCode { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public CreditNoteRefundChannel Channel { get; private set; }
    public CreditNoteRefundStatus Status { get; private set; }
    public string Reason { get; private set; } = null!;
    public string? ProviderReference { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? FailureReason { get; private set; }

    public static CreditNoteRefund Create(Guid organizationId, Guid invoiceId, Guid creditNoteId,
        Guid paymentId, string currencyCode, decimal amount, CreditNoteRefundChannel channel,
        string reason, string? providerReference, DateTimeOffset now)
    {
        if (organizationId == Guid.Empty || invoiceId == Guid.Empty || creditNoteId == Guid.Empty || paymentId == Guid.Empty)
            throw new DomainException("Invoice, credit note and payment are required.");
        if (amount <= 0 || MoneyMath.Round(amount) != amount)
            throw new DomainException("Refund amount must be greater than zero and use at most 2 decimals.");
        if (!Enum.IsDefined(channel)) throw new DomainException("Refund channel is not valid.");

        return new CreditNoteRefund
        {
            OrganizationId = organizationId,
            InvoiceId = invoiceId,
            CreditNoteId = creditNoteId,
            PaymentId = paymentId,
            CurrencyCode = Guard.IsoCode(currencyCode, 3, nameof(CurrencyCode)),
            Amount = amount,
            Channel = channel,
            Status = channel == CreditNoteRefundChannel.External ? CreditNoteRefundStatus.Processed : CreditNoteRefundStatus.Pending,
            Reason = Guard.NotBlank(reason, nameof(Reason), 500),
            ProviderReference = Guard.OptionalText(providerReference, nameof(ProviderReference), 100),
            ProcessedAt = channel == CreditNoteRefundChannel.External ? now : null
        };
    }

    public void ApplyProviderStatus(string status, string? failureReason, DateTimeOffset now)
    {
        if (Channel != CreditNoteRefundChannel.Paystack || Status == CreditNoteRefundStatus.Processed)
            return;

        Status = status.ToLowerInvariant() switch
        {
            "processing" => CreditNoteRefundStatus.Processing,
            "needs-attention" => CreditNoteRefundStatus.NeedsAttention,
            "processed" => CreditNoteRefundStatus.Processed,
            "failed" => CreditNoteRefundStatus.Failed,
            _ => CreditNoteRefundStatus.Pending
        };
        FailureReason = Status == CreditNoteRefundStatus.Failed
            ? Guard.OptionalText(failureReason, nameof(FailureReason), 500) ?? "The payment provider could not complete the refund."
            : null;
        ProcessedAt = Status == CreditNoteRefundStatus.Processed ? now : null;
    }
}
