using Domain.Common;

namespace Domain.Banking;

public enum StatementLineStatus { Unmatched, Matched, Ignored }

public sealed class BankStatementLine : BaseEntity, ITenantOwned
{
    private BankStatementLine() { }

    public Guid OrganizationId { get; private set; }
    public Guid StatementBatchId { get; private set; }
    public int RowNumber { get; private set; }
    public DateOnly TransactionDate { get; private set; }
    public string Description { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = null!;
    public string? Reference { get; private set; }
    public StatementLineStatus Status { get; private set; }
    public Guid? MatchedPaymentId { get; private set; }
    public DateTimeOffset? ReconciledAt { get; private set; }
    public Guid? ReconciledByUserId { get; private set; }
    public string? ResolutionNote { get; private set; }

    public static BankStatementLine Create(Guid organizationId, Guid batchId, int rowNumber,
        DateOnly transactionDate, string description, decimal amount, string currencyCode, string? reference)
    {
        if (organizationId == Guid.Empty || batchId == Guid.Empty || rowNumber < 2)
            throw new DomainException("Organization, statement batch and source row are required.");
        if (amount == 0 || MoneyMath.Round(amount) != amount)
            throw new DomainException("Statement amount must be non-zero and have at most 2 decimal places.");
        return new BankStatementLine
        {
            OrganizationId = organizationId,
            StatementBatchId = batchId,
            RowNumber = rowNumber,
            TransactionDate = transactionDate,
            Description = Guard.NotBlank(description, nameof(Description), 500),
            Amount = amount,
            CurrencyCode = Guard.IsoCode(currencyCode, 3, nameof(CurrencyCode)),
            Reference = Guard.OptionalText(reference, nameof(Reference), 100),
            Status = StatementLineStatus.Unmatched
        };
    }

    public void MatchPayment(Guid paymentId, Guid userId, DateTimeOffset now)
    {
        EnsureUnresolved();
        if (paymentId == Guid.Empty || userId == Guid.Empty)
            throw new DomainException("A payment and user are required to reconcile this line.");
        MatchedPaymentId = paymentId;
        ReconciledByUserId = userId;
        ReconciledAt = now;
        Status = StatementLineStatus.Matched;
    }

    public void Ignore(string reason, Guid userId, DateTimeOffset now)
    {
        EnsureUnresolved();
        if (userId == Guid.Empty) throw new DomainException("A user is required to resolve this line.");
        ResolutionNote = Guard.NotBlank(reason, nameof(ResolutionNote), 500);
        ReconciledByUserId = userId;
        ReconciledAt = now;
        Status = StatementLineStatus.Ignored;
    }

    private void EnsureUnresolved()
    {
        if (Status != StatementLineStatus.Unmatched)
            throw new DomainException("This statement line has already been resolved.");
    }
}
