using Domain.Bills;

namespace Application.Bills;

public sealed record BillLineInput(
    string Description, decimal Quantity, decimal UnitPrice,
    decimal DiscountPercent = 0m, decimal TaxRatePercent = 0m);

public sealed record BillInput(
    Guid SupplierId, string Number, DateOnly BillDate, DateOnly? DueDate, string? Notes,
    IReadOnlyList<BillLineInput> Lines, string? CurrencyCode = null, decimal? ExchangeRateToBase = null);

public sealed record FinalizeBillCommand(bool ConfirmPossibleDuplicates);
public sealed record VoidBillCommand(string Reason);

public sealed record ListBillsQuery(
    string? Search, BillStatus? Status, Guid? SupplierId, bool? Overdue, DateOnly? DueBy, int Page, int PageSize);

public sealed record BillSearch(
    string? Search, BillStatus? Status, Guid? SupplierId, bool? Overdue, DateOnly? DueBy);

public sealed record BillLineDto(
    int Position, string Description, decimal Quantity, decimal UnitPrice,
    decimal DiscountPercent, decimal TaxRatePercent,
    decimal LineSubtotal, decimal DiscountAmount, decimal TaxAmount, decimal LineTotal);

public sealed record PossibleDuplicateDto(
    Guid BillId, string Number, DateOnly BillDate, decimal Total, string Reason);

public sealed record BillDto(
    Guid Id, string Number, Guid SupplierId, string SupplierName, string CurrencyCode,
    string BaseCurrencyCode, decimal ExchangeRateToBase,
    BillStatus Status, DateOnly BillDate, DateOnly DueDate, string? Notes,
    decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal Total,
    decimal AmountPaid, decimal Balance, bool IsOverdue, int DaysOverdue, string? VoidReason,
    IReadOnlyList<BillLineDto> Lines, IReadOnlyList<PossibleDuplicateDto> PossibleDuplicates);

public sealed record BillSummaryDto(
    Guid Id, string Number, Guid SupplierId, string SupplierName, string CurrencyCode,
    string BaseCurrencyCode, decimal ExchangeRateToBase,
    BillStatus Status, DateOnly BillDate, DateOnly DueDate,
    decimal Total, decimal AmountPaid, decimal Balance, bool IsOverdue);

public interface IBillRepository
{
    Task<SupplierBill?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // Is there another non-void bill with this number from this supplier? (Guid.Empty = exclude nothing)
    Task<bool> NumberInUseAsync(
        Guid supplierId, string numberKey, Guid excludeBillId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PossibleDuplicateDto>> FindPossibleDuplicatesAsync(
        Guid supplierId, decimal total, DateOnly billDate, Guid excludeBillId,
        CancellationToken cancellationToken = default);

    // Locks the bill row until the current transaction ends. False if it is not visible to this organization.
    Task<bool> LockAsync(Guid id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<BillSummaryDto> Items, int Total)> SearchAsync(
        BillSearch filter, DateOnly today, int skip, int take, CancellationToken cancellationToken = default);

    void Add(SupplierBill bill);
}
