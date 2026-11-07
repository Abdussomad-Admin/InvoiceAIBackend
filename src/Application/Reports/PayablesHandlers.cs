using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Invoices;
using Application.Organizations;
using Application.Suppliers;
using Domain.Common;

namespace Application.Reports;

public sealed class GetSupplierSummaryHandler(
    ISupplierRepository suppliers, IPayablesRepository payables, IOrganizationRepository organizations,
    ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<SupplierSummaryDto> Handle(Guid supplierId, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var supplier = await suppliers.GetByIdAsync(supplierId, cancellationToken)
            ?? throw new NotFoundException("Supplier not found.");

        var today = InvoiceLookups.Today(organization, clock);
        var totals = await payables.GetSupplierTotalsAsync(supplierId, cancellationToken);
        var open = await payables.GetOpenBillsAsync(supplierId, cancellationToken);
        var overdue = open.Where(r => r.DueDate < today).ToList();

        return new SupplierSummaryDto(
            supplier.Id, supplier.Name, organization.BaseCurrencyCode,
            totals.Billed, totals.Paid, open.Sum(r => r.Balance), overdue.Sum(r => r.Balance),
            open.Count, overdue.Count);
    }
}

public sealed class GetPayablesAgingHandler(
    IPayablesRepository payables, IOrganizationRepository organizations,
    ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<PayablesAgingReportDto> Handle(CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var today = InvoiceLookups.Today(organization, clock);
        var rows = await payables.GetOpenBillsAsync(null, cancellationToken);

        var buckets = Enum.GetValues<AgingBucket>().Select(bucket =>
        {
            var inBucket = rows.Where(r => Aging.Classify(r.DueDate, today) == bucket).ToList();
            return new AgingBucketDto(bucket, Aging.Label(bucket), inBucket.Count, inBucket.Sum(r => r.Balance));
        }).ToList();

        decimal Sum(IEnumerable<OpenBillRow> source, AgingBucket bucket)
            => source.Where(r => Aging.Classify(r.DueDate, today) == bucket).Sum(r => r.Balance);

        var suppliers = rows
            .GroupBy(r => new { r.SupplierId, r.SupplierName })
            .Select(g => new SupplierAgingDto(
                g.Key.SupplierId, g.Key.SupplierName,
                Sum(g, AgingBucket.Current), Sum(g, AgingBucket.Days1To30), Sum(g, AgingBucket.Days31To60),
                Sum(g, AgingBucket.Days61To90), Sum(g, AgingBucket.Over90), g.Sum(r => r.Balance)))
            .OrderByDescending(s => s.Total)
            .ToList();

        // Upcoming obligations: not yet overdue (due today or later) and due within the window.
        decimal DueWithin(int days)
            => rows.Where(r => r.DueDate >= today && r.DueDate <= today.AddDays(days)).Sum(r => r.Balance);

        var overdue = buckets.Where(b => b.Bucket != AgingBucket.Current).Sum(b => b.Balance);

        return new PayablesAgingReportDto(
            today, organization.BaseCurrencyCode, rows.Sum(r => r.Balance), overdue,
            DueWithin(7), DueWithin(30), buckets, suppliers);
    }
}

public sealed class GetExpenseReportHandler(
    IPayablesRepository payables, IOrganizationRepository organizations,
    ICurrentOrganization currentOrganization, TimeProvider clock)
{
    private static readonly IReadOnlyList<MetricDefinitionDto> Definitions =
    [
        new("netExpenses", "Sum of bill subtotals less discounts, excluding tax, for finalized supplier bills dated within the period. Drafts and voided bills are excluded."),
        new("taxOnExpenses", "Tax charged on those same bills."),
        new("grossBilled", "Net expenses plus tax: the total of those bills."),
        new("cashPaid", "Sum of payments to suppliers dated within the period. Reversed payments are excluded."),
        new("monthly", "Billed amounts are grouped by bill date and paid amounts by payment date."),
        new("topSuppliers", "The five suppliers with the largest net billed amount in the period.")
    ];

    public async Task<ExpenseReportDto> Handle(ExpenseQuery query, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var today = InvoiceLookups.Today(organization, clock);

        var to = query.To ?? today;
        var from = query.From ?? new DateOnly(today.Year, today.Month, 1).AddMonths(-2);

        if (from > to)
            throw new DomainException("'from' must not be after 'to'.");

        var months = DashboardMath.Months(from, to);
        if (months.Count > DashboardMath.MaxMonths)
            throw new DomainException($"The date range can span at most {DashboardMath.MaxMonths} months.");

        var billed = await payables.GetBilledAsync(from, to, cancellationToken);
        var paid = await payables.GetPaidAsync(from, to, cancellationToken);

        var monthly = months.Select(month =>
        {
            var billedInMonth = billed.Where(x => DashboardMath.MonthKey(x.BillDate) == month).ToList();
            var paidInMonth = paid.Where(x => DashboardMath.MonthKey(x.Date) == month).ToList();

            return new MonthlyExpenseDto(
                month, billedInMonth.Sum(x => x.Net), billedInMonth.Sum(x => x.Tax),
                billedInMonth.Sum(x => x.Gross), paidInMonth.Sum(x => x.Amount));
        }).ToList();

        var top = billed
            .GroupBy(x => new { x.SupplierId, x.SupplierName })
            .Select(g => new TopSupplierSpendDto(g.Key.SupplierId, g.Key.SupplierName, g.Sum(x => x.Net), g.Count()))
            .OrderByDescending(s => s.NetBilled)
            .Take(5)
            .ToList();

        return new ExpenseReportDto(
            from, to, organization.BaseCurrencyCode,
            billed.Sum(x => x.Net), billed.Sum(x => x.Tax), billed.Sum(x => x.Gross), billed.Count,
            paid.Sum(x => x.Amount), paid.Count,
            monthly, top, Definitions);
    }
}