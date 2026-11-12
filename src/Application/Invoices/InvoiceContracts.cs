using Domain.Invoices;

namespace Application.Invoices;

public sealed record InvoiceLineInput(
    string Description, decimal Quantity, decimal UnitPrice,
    decimal DiscountPercent = 0m, decimal TaxRatePercent = 0m);

public sealed record InvoiceInput(
    Guid CustomerId, int? PaymentTermsDays, string? Notes, IReadOnlyList<InvoiceLineInput> Lines,
    string? CurrencyCode = null, decimal? ExchangeRateToBase = null);

public sealed record IssueInvoiceCommand(DateOnly? DueDate);
public sealed record VoidInvoiceCommand(string Reason);
public sealed record CreateCreditNoteCommand(decimal Amount, string Reason);
public sealed record CreditNoteDto(Guid Id, Guid InvoiceId, string InvoiceNumber, string Number,
    string CurrencyCode, string BaseCurrencyCode, decimal ExchangeRateToBase,
    decimal Amount, string Reason, DateOnly IssuedOn, DateTimeOffset CreatedAt);
public sealed record CreditNoteRefundCommand(Guid PaymentId, decimal Amount, string Reason,
    CreditNoteRefundChannel Channel, string? Reference = null);
public sealed record CreditNoteRefundDto(Guid Id, Guid InvoiceId, Guid CreditNoteId, Guid PaymentId,
    string CurrencyCode, decimal Amount, CreditNoteRefundChannel Channel, CreditNoteRefundStatus Status,
    string Reason, string? ProviderReference, DateTimeOffset CreatedAt, DateTimeOffset? ProcessedAt, string? FailureReason);

public sealed record ListInvoicesQuery(
    string? Search, InvoiceStatus? Status, Guid? CustomerId, bool? Overdue, int Page, int PageSize);

public sealed record InvoiceSearch(string? Search, InvoiceStatus? Status, Guid? CustomerId, bool? Overdue);

public sealed record InvoiceLineDto(
    int Position, string Description, decimal Quantity, decimal UnitPrice,
    decimal DiscountPercent, decimal TaxRatePercent,
    decimal LineSubtotal, decimal DiscountAmount, decimal TaxAmount, decimal LineTotal);

public sealed record InvoiceDto(
    Guid Id, string? Number, Guid CustomerId, string CustomerName, string CurrencyCode,
    InvoiceStatus Status, DateOnly? IssueDate, DateOnly? DueDate, int PaymentTermsDays, string? Notes,
    decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal Total,
    decimal AmountPaid, decimal AmountCredited, decimal Balance, string BaseCurrencyCode, decimal ExchangeRateToBase,
    bool IsOverdue, int DaysOverdue, string? VoidReason,
    IReadOnlyList<InvoiceLineDto> Lines, DateTimeOffset? LastSentAt, int SentCount);

public sealed record InvoiceSummaryDto(
    Guid Id, string? Number, Guid CustomerId, string CustomerName, string CurrencyCode,
    InvoiceStatus Status, DateOnly? IssueDate, DateOnly? DueDate,
    decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue);

public sealed record InvoiceExportRow(
    string? Number, string CustomerName, string CurrencyCode, InvoiceStatus Status,
    DateOnly? IssueDate, DateOnly? DueDate, decimal Total, decimal AmountPaid,
    decimal Balance, DateTimeOffset CreatedAt);

public interface IInvoiceRepository
{
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<InvoiceSummaryDto> Items, int Total)> SearchAsync(
        InvoiceSearch filter, DateOnly today, int skip, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InvoiceExportRow>> ExportAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CreditNoteDto>> CreditNotesAsync(Guid invoiceId, CancellationToken cancellationToken = default);
    Task<CreditNoteDto?> CreditNoteByIdAsync(Guid creditNoteId, CancellationToken cancellationToken = default);
    void AddCreditNote(CreditNote creditNote);

    // Locks the invoice row until the current transaction ends. False if it is not visible to this organization.
    Task<bool> LockAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Invoice invoice);
}

public sealed record CreditNotePdfModel(PdfParty Seller, PdfParty Buyer, string InvoiceNumber,
    string CreditNoteNumber, string CurrencyCode, decimal Amount, string Reason, DateOnly IssuedOn);
public sealed record CreditNotePdfFile(byte[] Content, string FileName);
public interface ICreditNotePdfRenderer { byte[] Render(CreditNotePdfModel model); }

public interface IInvoiceNumberGenerator
{
    Task<string> NextAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
