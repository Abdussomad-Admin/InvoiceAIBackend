using Domain.Banking;

namespace Application.Banking;

public sealed record BankStatementImportRow(int RowNumber, string? TransactionDate, string? Description,
    string? Amount, string? CurrencyCode, string? Reference);
public sealed record BankStatementImportError(int RowNumber, string Message);
public sealed record BankStatementImportResult(Guid? BatchId, int RowsRead, int Imported,
    IReadOnlyList<BankStatementImportError> Errors);

public sealed record BankStatementBatchDto(Guid Id, string FileName, int ImportedLineCount, DateTimeOffset ImportedAt);
public sealed record BankStatementLineDto(Guid Id, int RowNumber, DateOnly TransactionDate, string Description,
    decimal Amount, string CurrencyCode, string? Reference, StatementLineStatus Status,
    Guid? MatchedPaymentId, DateTimeOffset? ReconciledAt, string? ResolutionNote);
public sealed record BankStatementDetailsDto(BankStatementBatchDto Batch, IReadOnlyList<BankStatementLineDto> Lines);
public sealed record BankPaymentCandidate(Guid PaymentId, string? InvoiceNumber, string CustomerName,
    decimal Amount, string CurrencyCode, DateOnly PaidOn, string? Reference, bool ReferenceMatches);
public sealed record BankInvoiceCandidate(Guid InvoiceId, string? InvoiceNumber, string CustomerName,
    decimal Balance, string CurrencyCode, DateOnly? DueDate);
public sealed record BankStatementSuggestionsDto(IReadOnlyList<BankPaymentCandidate> Payments,
    IReadOnlyList<BankInvoiceCandidate> Invoices);
public sealed record MatchStatementPaymentCommand(Guid PaymentId);
public sealed record IgnoreStatementLineCommand(string Reason);

public interface IBankStatementRepository
{
    Task<bool> FileHashExistsAsync(string fileHash, CancellationToken cancellationToken = default);
    Task<BankStatementBatch?> GetBatchAsync(Guid batchId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BankStatementLine>> GetLinesAsync(Guid batchId, CancellationToken cancellationToken = default);
    Task<BankStatementLine?> GetLineAsync(Guid lineId, CancellationToken cancellationToken = default);
    Task<bool> LockLineAsync(Guid lineId, CancellationToken cancellationToken = default);
    Task<bool> LockPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<bool> PaymentCanMatchAsync(BankStatementLine line, Guid paymentId, CancellationToken cancellationToken = default);
    Task<BankStatementSuggestionsDto> GetSuggestionsAsync(BankStatementLine line, CancellationToken cancellationToken = default);
    void Add(BankStatementBatch batch);
}
