using Application.Reports;

namespace Tests;

public sealed class CashFlowForecastTests
{
    [Fact]
    public void Build_ForecastsOnlyOutstandingItemsDueWithinThirtyDays_AndSeparatesOverdueBalances()
    {
        var today = new DateOnly(2026, 11, 13);
        var invoices = new[]
        {
            Invoice(today.AddDays(-1), 100m),
            Invoice(today, 250m),
            Invoice(today.AddDays(30), 400m),
            Invoice(today.AddDays(31), 500m)
        };
        var bills = new[]
        {
            Bill(today.AddDays(-5), 75m),
            Bill(today.AddDays(7), 50m),
            Bill(today.AddDays(31), 25m)
        };

        var result = CashFlowForecastBuilder.Build(today, "NGN", invoices, bills);

        Assert.Equal(today.AddDays(30), result.Through);
        Assert.Equal("NGN", result.CurrencyCode);
        Assert.Equal(650m, result.ExpectedIncoming);
        Assert.Equal(50m, result.ExpectedOutgoing);
        Assert.Equal(600m, result.ExpectedNetMovement);
        Assert.Equal(100m, result.OverdueIncoming);
        Assert.Equal(75m, result.OverdueOutgoing);
        Assert.Equal(3, result.ScheduledItems.Count);
        Assert.Equal(today, result.ScheduledItems[0].DueDate);
        Assert.Contains(result.Assumptions, note => note.Contains("not a bank balance"));
    }

    private static OpenInvoiceRow Invoice(DateOnly dueDate, decimal balance)
        => new(Guid.NewGuid(), "INV-1", Guid.NewGuid(), "Customer", dueDate, "NGN", balance, balance);

    private static OpenBillRow Bill(DateOnly dueDate, decimal balance)
        => new(Guid.NewGuid(), "BILL-1", Guid.NewGuid(), "Supplier", dueDate, balance);
}
