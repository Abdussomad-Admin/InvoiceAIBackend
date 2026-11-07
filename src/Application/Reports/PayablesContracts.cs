namespace Application.Reports;

public sealed record OpenBillRow(
    Guid BillId, string Number, Guid SupplierId, string SupplierName, DateOnly DueDate, decimal Balance);

public sealed record SupplierTotals(decimal Billed, decimal Paid);

public sealed record SupplierSummaryDto(
    Guid SupplierId, string SupplierName, string CurrencyCode,
    decimal TotalBilled, decimal TotalPaid, decimal Outstanding, decimal Overdue,
    int OpenBills, int OverdueBills);

public sealed record SupplierAgingDto(
    Guid SupplierId, string SupplierName,
    decimal Current, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Over90, decimal Total);

public sealed record PayablesAgingReportDto(
    DateOnly AsOf, string CurrencyCode, decimal TotalOutstanding, decimal TotalOverdue,
    decimal DueWithin7Days, decimal DueWithin30Days,
    IReadOnlyList<AgingBucketDto> Buckets, IReadOnlyList<SupplierAgingDto> Suppliers);

public sealed record ExpenseQuery(DateOnly? From, DateOnly? To);

public sealed record BilledRow(
    Guid SupplierId, string SupplierName, DateOnly BillDate, decimal Net, decimal Tax, decimal Gross);

public sealed record MonthlyExpenseDto(string Month, decimal NetBilled, decimal Tax, decimal GrossBilled, decimal Paid);

public sealed record TopSupplierSpendDto(Guid SupplierId, string SupplierName, decimal NetBilled, int BillCount);

public sealed record ExpenseReportDto(
    DateOnly From, DateOnly To, string CurrencyCode,
    decimal NetExpenses, decimal TaxOnExpenses, decimal GrossBilled, int BillCount,
    decimal CashPaid, int PaymentCount,
    IReadOnlyList<MonthlyExpenseDto> Monthly, IReadOnlyList<TopSupplierSpendDto> TopSuppliers,
    IReadOnlyList<MetricDefinitionDto> Definitions);

public interface IPayablesRepository
{
    Task<IReadOnlyList<OpenBillRow>> GetOpenBillsAsync(Guid? supplierId, CancellationToken cancellationToken = default);
    Task<SupplierTotals> GetSupplierTotalsAsync(Guid supplierId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BilledRow>> GetBilledAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DatedAmount>> GetPaidAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}