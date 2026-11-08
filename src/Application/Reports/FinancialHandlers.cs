using Application.Common.Interfaces;
using Application.Invoices;
using Application.Organizations;
using Domain.Common;

namespace Application.Reports;

internal sealed record FinancialData(
    IReadOnlyList<SalesRow> Sales, IReadOnlyList<BilledRow> Billed,
    IReadOnlyList<DatedAmount> Collected, IReadOnlyList<DatedAmount> Paid);

internal static class FinancialInfo
{
    public static readonly IReadOnlyList<MetricDefinitionDto> Definitions =
    [
        new("income", "Net sales: invoice subtotals less discounts, excluding tax, for invoices issued within the period. Drafts and voided invoices are excluded."),
        new("expenses", "Net expenses: supplier bill subtotals less discounts, excluding tax, for finalized bills dated within the period. Drafts and voided bills are excluded."),
        new("estimatedProfit", "Income minus expenses (accrual basis). An estimate, not an accounting profit."),
        new("expensesExceedIncome", "True when expenses are greater than income for the period."),
        new("cashReceived", "Customer payments dated within the period, including tax. Reversed payments are excluded."),
        new("cashPaidOut", "Supplier payments dated within the period, including tax. Reversed payments are excluded."),
        new("netCashFlow", "Cash received minus cash paid out. It includes tax, so it is not comparable to estimated profit.")
    ];

    public static readonly IReadOnlyList<string> Caveats =
    [
        "Profit is an estimate based only on invoices and supplier bills recorded in this app. Costs such as salaries, rent and tax payments are not included unless recorded as supplier bills.",
        "All amounts are in the organization's base currency.",
        "This is a management summary, not a statutory accounting report."
    ];
}

internal static class FinancialCalculator
{
    public static async Task<FinancialData> LoadAsync(
        IDashboardRepository dashboard, IPayablesRepository payables,
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
        => new(
            await dashboard.GetSalesAsync(from, to, cancellationToken),
            await payables.GetBilledAsync(from, to, cancellationToken),
            await dashboard.GetCollectedAsync(from, to, cancellationToken),
            await payables.GetPaidAsync(from, to, cancellationToken));

    public static FinancialPeriodDto Period(FinancialData data, DateOnly from, DateOnly to)
    {
        var sales = data.Sales.Where(x => x.Date >= from && x.Date <= to).ToList();
        var billed = data.Billed.Where(x => x.BillDate >= from && x.BillDate <= to).ToList();
        var collected = data.Collected.Where(x => x.Date >= from && x.Date <= to).ToList();
        var paid = data.Paid.Where(x => x.Date >= from && x.Date <= to).ToList();

        var income = sales.Sum(x => x.Net);
        var expenses = billed.Sum(x => x.Net);
        var received = collected.Sum(x => x.Amount);
        var paidOut = paid.Sum(x => x.Amount);

        return new FinancialPeriodDto(
            from, to,
            income, sales.Sum(x => x.Tax), sales.Count,
            expenses, billed.Sum(x => x.Tax), billed.Count,
            income - expenses, expenses > income,
            received, paidOut, received - paidOut);
    }

    public static IReadOnlyList<MonthlyFinancialDto> Monthly(FinancialData data, DateOnly from, DateOnly to)
        => DashboardMath.Months(from, to).Select(month =>
        {
            var income = data.Sales
                .Where(x => x.Date >= from && x.Date <= to && DashboardMath.MonthKey(x.Date) == month)
                .Sum(x => x.Net);
            var expenses = data.Billed
                .Where(x => x.BillDate >= from && x.BillDate <= to && DashboardMath.MonthKey(x.BillDate) == month)
                .Sum(x => x.Net);
            var received = data.Collected
                .Where(x => x.Date >= from && x.Date <= to && DashboardMath.MonthKey(x.Date) == month)
                .Sum(x => x.Amount);
            var paidOut = data.Paid
                .Where(x => x.Date >= from && x.Date <= to && DashboardMath.MonthKey(x.Date) == month)
                .Sum(x => x.Amount);

            return new MonthlyFinancialDto(
                month, income, expenses, income - expenses, received, paidOut, received - paidOut);
        }).ToList();
}

public sealed class GetFinancialSummaryHandler(
    IDashboardRepository dashboard, IPayablesRepository payables, IOrganizationRepository organizations,
    ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<FinancialSummaryDto> Handle(FinancialSummaryQuery query, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var today = InvoiceLookups.Today(organization, clock);

        // With no dates, the period is the current month to date.
        var to = query.To ?? today;
        var from = query.From ?? new DateOnly(to.Year, to.Month, 1);

        if (from > to)
            throw new DomainException("'from' must not be after 'to'.");
        if (DashboardMath.Months(from, to).Count > DashboardMath.MaxMonths)
            throw new DomainException($"The date range can span at most {DashboardMath.MaxMonths} months.");

        var data = await FinancialCalculator.LoadAsync(dashboard, payables, from, to, cancellationToken);

        return new FinancialSummaryDto(
            organization.BaseCurrencyCode,
            FinancialCalculator.Period(data, from, to),
            FinancialCalculator.Monthly(data, from, to),
            FinancialInfo.Definitions, FinancialInfo.Caveats);
    }
}

public sealed class GetYearComparisonHandler(
    IDashboardRepository dashboard, IPayablesRepository payables, IOrganizationRepository organizations,
    ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<YearComparisonDto> Handle(YearComparisonQuery query, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var today = InvoiceLookups.Today(organization, clock);

        var year = query.Year ?? today.Year;
        if (year < 2000 || year > today.Year)
            throw new DomainException($"The year must be between 2000 and {today.Year}.");

        var currentStart = new DateOnly(year, 1, 1);
        var currentEnd = year == today.Year ? today : new DateOnly(year, 12, 31);
        var previousStart = new DateOnly(year - 1, 1, 1);
        var previousSameEnd = FinancialMath.SameDayLastYear(currentEnd);
        var previousFullEnd = new DateOnly(year - 1, 12, 31);

        // One load covers last year and this year, and the three periods are cut from it in memory.
        var data = await FinancialCalculator.LoadAsync(dashboard, payables, previousStart, currentEnd, cancellationToken);

        var current = FinancialCalculator.Period(data, currentStart, currentEnd);
        var previousSame = FinancialCalculator.Period(data, previousStart, previousSameEnd);
        var previousFull = FinancialCalculator.Period(data, previousStart, previousFullEnd);

        return new YearComparisonDto(
            year, organization.BaseCurrencyCode, current, previousSame, previousFull,
            FinancialMath.Compare(current.Income, previousSame.Income),
            FinancialMath.Compare(current.Expenses, previousSame.Expenses),
            FinancialMath.Compare(current.EstimatedProfit, previousSame.EstimatedProfit),
            FinancialMath.Compare(current.NetCashFlow, previousSame.NetCashFlow),
            FinancialInfo.Definitions, FinancialInfo.Caveats);
    }
}