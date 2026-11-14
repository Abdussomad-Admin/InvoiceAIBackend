using Application.Reports;

namespace Tests;

public sealed class ProfitOutlookTests
{
    [Fact]
    public void Build_ProjectsAverageOfThreeCompleteMonthsIntoNextMonth()
    {
        var today = new DateOnly(2026, 11, 14);
        var history = Summary(
            Month("2026-08", 1000m, 400m),
            Month("2026-09", 1200m, 500m),
            Month("2026-10", 800m, 300m));

        var result = ProfitOutlookBuilder.Build(today, history);

        Assert.True(result.HasRecordedActivity);
        Assert.Equal(new DateOnly(2026, 12, 1), result.ProjectedPeriodFrom);
        Assert.Equal(new DateOnly(2026, 12, 31), result.ProjectedPeriodTo);
        Assert.Equal(3, result.BaselineMonths);
        Assert.Equal(1000m, result.AverageMonthlyIncome);
        Assert.Equal(400m, result.AverageMonthlyExpenses);
        Assert.Equal(600m, result.ProjectedEstimatedProfit);
        Assert.Equal(3, result.Baseline.Count);
    }

    [Fact]
    public void Build_DoesNotInventAnOutlookWithoutRecordedActivity()
    {
        var history = Summary(
            Month("2026-08", 0m, 0m),
            Month("2026-09", 0m, 0m),
            Month("2026-10", 0m, 0m));

        var result = ProfitOutlookBuilder.Build(new DateOnly(2026, 11, 14), history);

        Assert.False(result.HasRecordedActivity);
        Assert.Null(result.AverageMonthlyIncome);
        Assert.Null(result.AverageMonthlyExpenses);
        Assert.Null(result.ProjectedEstimatedProfit);
        Assert.Contains(result.Limitations, note => note.Contains("no recorded", StringComparison.OrdinalIgnoreCase));
    }

    private static MonthlyFinancialDto Month(string month, decimal income, decimal expenses)
        => new(month, income, expenses, income - expenses, 0m, 0m, 0m);

    private static FinancialSummaryDto Summary(params MonthlyFinancialDto[] months)
    {
        var period = new FinancialPeriodDto(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 31),
            months.Sum(item => item.Income), 0m, 0,
            months.Sum(item => item.Expenses), 0m, 0,
            months.Sum(item => item.EstimatedProfit), false,
            0m, 0m, 0m);
        return new FinancialSummaryDto("NGN", period, months, [], []);
    }
}
