using Application.Common.Interfaces;
using Application.Invoices;
using Application.Organizations;

namespace Application.Reports;

public sealed record ProfitOutlookMonthDto(string Month, decimal RecordedIncome, decimal RecordedExpenses, decimal EstimatedProfit);

public sealed record ProfitOutlookDto(
    DateOnly AsOf, DateOnly ProjectedPeriodFrom, DateOnly ProjectedPeriodTo,
    string CurrencyCode, bool HasRecordedActivity, int BaselineMonths,
    decimal? AverageMonthlyIncome, decimal? AverageMonthlyExpenses, decimal? ProjectedEstimatedProfit,
    IReadOnlyList<ProfitOutlookMonthDto> Baseline, IReadOnlyList<string> Limitations);

public static class ProfitOutlookBuilder
{
    public static ProfitOutlookDto Build(DateOnly today, FinancialSummaryDto history)
    {
        var nextMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        var projectedTo = nextMonth.AddMonths(1).AddDays(-1);
        var months = history.Monthly;
        var activity = months.Any(month => month.Income != 0m || month.Expenses != 0m);
        var baseline = months.Select(month => new ProfitOutlookMonthDto(
            month.Month, month.Income, month.Expenses, month.EstimatedProfit)).ToList();
        var limitations = new[]
        {
            "This is a simple run-rate estimate: the average of the previous three complete calendar months, applied to the next full calendar month.",
            "It uses only net sales from issued invoices and net costs from finalized supplier bills recorded in Invoice AI. Unrecorded costs such as salaries, rent and taxes are excluded.",
            "It does not account for seasonality, new or lost customers, future orders, planned spending or changing business conditions. Actual results may differ substantially."
        };

        if (!activity)
            return new ProfitOutlookDto(today, nextMonth, projectedTo, history.CurrencyCode, false, months.Count,
                null, null, null, baseline,
                limitations.Prepend("There is no recorded invoice or supplier-bill activity in the baseline period, so a profit outlook cannot be calculated.").ToArray());

        var divisor = months.Count;
        var averageIncome = months.Sum(month => month.Income) / divisor;
        var averageExpenses = months.Sum(month => month.Expenses) / divisor;
        return new ProfitOutlookDto(today, nextMonth, projectedTo, history.CurrencyCode, true, divisor,
            averageIncome, averageExpenses, averageIncome - averageExpenses, baseline, limitations);
    }
}

public sealed class GetProfitOutlookHandler(
    GetFinancialSummaryHandler summary, IOrganizationRepository organizations,
    ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<ProfitOutlookDto> Handle(CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var today = InvoiceLookups.Today(organization, clock);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var baselineTo = currentMonth.AddDays(-1);
        var baselineFrom = currentMonth.AddMonths(-3);
        var history = await summary.Handle(new FinancialSummaryQuery(baselineFrom, baselineTo), cancellationToken);
        return ProfitOutlookBuilder.Build(today, history);
    }
}
