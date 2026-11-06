using Domain.Invoices;

namespace Application.Invoices;

public sealed record InvoiceLineInput(
    string Description, decimal Quantity, decimal UnitPrice,
    decimal DiscountPercent = 0m, decimal TaxRatePercent = 0m);

public sealed record InvoiceInput(
    Guid CustomerId, int? PaymentTermsDays, string? Notes, IReadOnlyList<InvoiceLineInput> Lines);

public sealed record IssueInvoiceCommand(DateOnly? DueDate);
public sealed record VoidInvoiceCommand(string Reason);

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
    decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue, string? VoidReason,
    IReadOnlyList<InvoiceLineDto> Lines, DateTimeOffset? LastSentAt, int SentCount);

public sealed record InvoiceSummaryDto(
    Guid Id, string? Number, Guid CustomerId, string CustomerName, string CurrencyCode,
    InvoiceStatus Status, DateOnly? IssueDate, DateOnly? DueDate,
    decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue);

public interface IInvoiceRepository
{
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<InvoiceSummaryDto> Items, int Total)> SearchAsync(
        InvoiceSearch filter, DateOnly today, int skip, int take, CancellationToken cancellationToken = default);

    // Locks the invoice row until the current transaction ends. False if it is not visible to this organization.
    Task<bool> LockAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Invoice invoice);
}

public interface IInvoiceNumberGenerator
{
    Task<string> NextAsync(Guid organizationId, CancellationToken cancellationToken = default);
}