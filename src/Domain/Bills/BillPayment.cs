using Domain.Common;
using Domain.Payments;

namespace Domain.Bills;

// Append-only, like customer payments: never edited or deleted. A mistake is reversed and the row is kept.
public sealed class BillPayment : BaseEntity, ITenantOwned
{
    private BillPayment() { }

    public Guid OrganizationId { get; private set; }
    public Guid BillId { get; private set; }
    public Guid SupplierId { get; private set; }
    public string CurrencyCode { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public DateOnly PaidOn { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? Reference { get; private set; }
    public string? Notes { get; private set; }
    public Guid RecordedByUserId { get; private set; }

    public DateTimeOffset? ReversedAt { get; private set; }
    public Guid? ReversedByUserId { get; private set; }
    public string? ReversalReason { get; private set; }

    public bool IsReversed => ReversedAt.HasValue;

    public static BillPayment Create(
        Guid organizationId, Guid billId, Guid supplierId, string currencyCode, decimal amount,
        DateOnly paidOn, PaymentMethod method, string? reference, string? notes, Guid recordedByUserId)
    {
        if (organizationId == Guid.Empty || billId == Guid.Empty || supplierId == Guid.Empty)
            throw new DomainException("Organization, bill and supplier are required.");
        if (recordedByUserId == Guid.Empty)
            throw new DomainException("The user recording the payment is required.");
        if (amount <= 0)
            throw new DomainException("The amount must be greater than zero.");
        if (MoneyMath.Round(amount) != amount)
            throw new DomainException("The amount can have at most 2 decimal places.");
        if (!Enum.IsDefined(method))
            throw new DomainException("Payment method is not valid.");

        return new BillPayment
        {
            OrganizationId = organizationId,
            BillId = billId,
            SupplierId = supplierId,
            CurrencyCode = Guard.IsoCode(currencyCode, 3, nameof(CurrencyCode)),
            Amount = amount,
            PaidOn = paidOn,
            Method = method,
            Reference = Guard.OptionalText(reference, nameof(Reference), 100),
            Notes = Guard.OptionalText(notes, nameof(Notes), 1000),
            RecordedByUserId = recordedByUserId
        };
    }

    public void Reverse(string reason, Guid userId, DateTimeOffset now)
    {
        if (IsReversed)
            throw new DomainException("This payment has already been reversed.");
        if (userId == Guid.Empty)
            throw new DomainException("The user reversing the payment is required.");

        ReversalReason = Guard.NotBlank(reason, nameof(ReversalReason), 500);
        ReversedByUserId = userId;
        ReversedAt = now;
    }
}