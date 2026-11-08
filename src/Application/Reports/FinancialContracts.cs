namespace Application.Reports;

public sealed record FinancialSummaryQuery(DateOnly? From, DateOnly? To);
public sealed record YearComparisonQuery(int? Year);

public sealed record FinancialPeriodDto(
    DateOnly From, DateOnly To,
    decimal Income, decimal TaxCharged, int InvoiceCount,
    decimal Expenses, decimal TaxOnExpenses, int BillCount,
    decimal EstimatedProfit, bool ExpensesExceedIncome,
    decimal CashReceived, decimal CashPaidOut, decimal NetCashFlow);

public sealed record MonthlyFinancialDto(
    string Month, decimal Income, decimal Expenses, decimal EstimatedProfit,
    decimal CashReceived, decimal CashPaidOut, decimal NetCashFlow);

public sealed record FinancialSummaryDto(
    string CurrencyCode, FinancialPeriodDto Period,
    IReadOnlyList<MonthlyFinancialDto> Monthly,
    IReadOnlyList<MetricDefinitionDto> Definitions, IReadOnlyList<string> Caveats);

public sealed record MetricComparisonDto(decimal Current, decimal Previous, decimal Change, decimal? ChangePercent);

public sealed record YearComparisonDto(
    int Year, string CurrencyCode,
    FinancialPeriodDto Current, FinancialPeriodDto PreviousSamePeriod, FinancialPeriodDto PreviousFullYear,
    MetricComparisonDto Income, MetricComparisonDto Expenses,
    MetricComparisonDto EstimatedProfit, MetricComparisonDto NetCashFlow,
    IReadOnlyList<MetricDefinitionDto> Definitions, IReadOnlyList<string> Caveats);

public static class FinancialMath
{
    // Relative to the size of the previous value, so a loss that shrinks reads as an improvement.
    // Null when the previous value is zero, because the percentage would be meaningless.
    public static decimal? PercentChange(decimal current, decimal previous)
        => previous == 0m
            ? null
            : Math.Round((current - previous) / Math.Abs(previous) * 100m, 1, MidpointRounding.AwayFromZero);

    public static MetricComparisonDto Compare(decimal current, decimal previous)
        => new(current, previous, current - previous, PercentChange(current, previous));

    // The same calendar day one year earlier. 29 February falls back to 28 February.
    public static DateOnly SameDayLastYear(DateOnly date)
        => new(date.Year - 1, date.Month, Math.Min(date.Day, DateTime.DaysInMonth(date.Year - 1, date.Month)));
}