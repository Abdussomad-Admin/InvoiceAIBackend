using Application.Invoices;
using Application.Reports;
using Domain.Common;
using Infrastructure.Persistence.Repositories;

namespace Tests;

public class FinancialMathTests
{
    [Theory]
    [InlineData(150, 100, 50.0)]
    [InlineData(50, 100, -50.0)]
    [InlineData(0, 100, -100.0)]
    [InlineData(-50, -100, 50.0)]   // a loss that halves is a 50% improvement
    public void PercentChange_IsRelativeToTheSizeOfThePreviousValue(double current, double previous, double expected)
    {
        var result = FinancialMath.PercentChange((decimal)current, (decimal)previous);

        Assert.Equal((decimal)expected, result!.Value);
    }

    [Fact]
    public void PercentChange_IsNullWhenThePreviousValueIsZero()
        => Assert.Null(FinancialMath.PercentChange(100m, 0m));

    [Fact]
    public void Compare_ReportsTheChange()
    {
        var comparison = FinancialMath.Compare(507.94m, 253.97m);

        Assert.Equal(253.97m, comparison.Change);
        Assert.Equal(100.0m, comparison.ChangePercent);
    }

    [Fact]
    public void SameDayLastYear_HandlesLeapDays()
    {
        Assert.Equal(new DateOnly(2025, 3, 15), FinancialMath.SameDayLastYear(new DateOnly(2026, 3, 15)));
        Assert.Equal(new DateOnly(2027, 2, 28), FinancialMath.SameDayLastYear(new DateOnly(2028, 2, 29)));
    }
}

public class FinancialSummaryTests
{
    private static readonly FixedTimeProvider Clock = PaymentTestData.Clock; // 15 January 2026

    private static async Task<FinancialSummaryDto> SummaryAsync(
        Seed seed, FinancialSummaryQuery query, TimeProvider? clock = null)
    {
        clock ??= Clock;
        await using var ctx = TestDatabase.CreateContext(clock, seed.OrganizationId, seed.UserId);
        var handler = new GetFinancialSummaryHandler(
            new DashboardRepository(ctx), new PayablesRepository(ctx), new OrganizationRepository(ctx),
            new TestCurrentOrganization(seed.OrganizationId), clock);
        return await handler.Handle(query, CancellationToken.None);
    }

    private static async Task<YearComparisonDto> ComparisonAsync(Seed seed, int? year)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var handler = new GetYearComparisonHandler(
            new DashboardRepository(ctx), new PayablesRepository(ctx), new OrganizationRepository(ctx),
            new TestCurrentOrganization(seed.OrganizationId), Clock);
        return await handler.Handle(new YearComparisonQuery(year), CancellationToken.None);
    }

    // Issues a standard invoice (273.02 gross, 253.97 net) as if today were `clock`'s date.
    private static async Task IssueInvoiceAtAsync(Seed seed, TimeProvider clock)
    {
        Guid id;
        await using (var ctx = TestDatabase.CreateContext(clock, seed.OrganizationId, seed.UserId))
        {
            var draft = await new InvoiceKit(ctx, seed.OrganizationId, clock).Create().Handle(
                new InvoiceInput(seed.CustomerId, null, null, PaymentTestData.Lines), CancellationToken.None);
            id = draft.Id;
        }

        await using (var ctx = TestDatabase.CreateContext(clock, seed.OrganizationId, seed.UserId))
        {
            await new InvoiceKit(ctx, seed.OrganizationId, clock).Issue().Handle(id, null, CancellationToken.None);
        }
    }

    private static FixedTimeProvider On(int year, int month, int day)
        => new(new DateTimeOffset(year, month, day, 10, 30, 0, TimeSpan.Zero));

    [Fact]
    public async Task Summary_ComputesIncomeExpensesProfitAndCashFlow()
    {
        var seed = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed);   // 253.97 net, 19.05 tax
        await PaymentTestData.RecordAsync(seed, invoice.Id, 100.00m);  // cash in
        var (_, bill) = await PayableTestData.OpenBillAsync(seed);     // 1000.00 net, 75.00 tax
        await PayableTestData.PayAsync(seed, bill.Id, 500.00m);        // cash out

        var summary = await SummaryAsync(seed, new FinancialSummaryQuery(null, null));
        var p = summary.Period;

        Assert.Equal("NGN", summary.CurrencyCode);
        Assert.Equal(new DateOnly(2026, 1, 1), p.From);
        Assert.Equal(new DateOnly(2026, 1, 15), p.To);

        Assert.Equal(253.97m, p.Income);
        Assert.Equal(19.05m, p.TaxCharged);
        Assert.Equal(1, p.InvoiceCount);
        Assert.Equal(1000.00m, p.Expenses);
        Assert.Equal(75.00m, p.TaxOnExpenses);
        Assert.Equal(1, p.BillCount);

        Assert.Equal(-746.03m, p.EstimatedProfit);
        Assert.True(p.ExpensesExceedIncome);

        Assert.Equal(100.00m, p.CashReceived);
        Assert.Equal(500.00m, p.CashPaidOut);
        Assert.Equal(-400.00m, p.NetCashFlow);

        var month = Assert.Single(summary.Monthly);
        Assert.Equal("2026-01", month.Month);
        Assert.Equal(p.EstimatedProfit, month.EstimatedProfit);

        Assert.NotEmpty(summary.Definitions);
        Assert.NotEmpty(summary.Caveats);
    }

    [Fact]
    public async Task WhenIncomeIsHigher_ThereIsAProfit_AndExpensesDoNotExceedIncome()
    {
        var seed = await PaymentTestData.SeedAsync();
        await PaymentTestData.IssueInvoiceAsync(seed);

        var p = (await SummaryAsync(seed, new FinancialSummaryQuery(null, null))).Period;

        Assert.Equal(253.97m, p.Income);
        Assert.Equal(0m, p.Expenses);
        Assert.Equal(253.97m, p.EstimatedProfit);
        Assert.False(p.ExpensesExceedIncome);
    }

    [Fact]
    public async Task DraftsVoidedItemsAndReversedPayments_AreLeftOut()
    {
        var seed = await PaymentTestData.SeedAsync();

        var counted = await PaymentTestData.IssueInvoiceAsync(seed);
        var payment = await PaymentTestData.RecordAsync(seed, counted.Id, 100.00m);
        await PaymentTestData.ReverseAsync(seed, payment.Id, "Bounced");

        var voided = await PaymentTestData.IssueInvoiceAsync(seed);
        await using (var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId))
        {
            await new InvoiceKit(ctx, seed.OrganizationId, Clock).Void().Handle(
                voided.Id, new VoidInvoiceCommand("Raised in error"), CancellationToken.None);
        }

        await PaymentTestData.CreateDraftAsync(seed);                    // a draft invoice
        var supplier = await BillTestData.SupplierAsync(seed);
        await BillTestData.BillAsync(seed, supplier.Id);                 // a draft bill

        var p = (await SummaryAsync(seed, new FinancialSummaryQuery(null, null))).Period;

        Assert.Equal(253.97m, p.Income);
        Assert.Equal(1, p.InvoiceCount);
        Assert.Equal(0m, p.Expenses);
        Assert.Equal(0, p.BillCount);
        Assert.Equal(0m, p.CashReceived);
    }

    [Fact]
    public async Task TheDateRange_SelectsThePeriod_AndBuildsOneRowPerMonth()
    {
        var seed = await PaymentTestData.SeedAsync();
        await PaymentTestData.IssueInvoiceAsync(seed); // issued 15 Jan 2026

        var january = await SummaryAsync(seed,
            new FinancialSummaryQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
        Assert.Equal(253.97m, january.Period.Income);

        var december = await SummaryAsync(seed,
            new FinancialSummaryQuery(new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31)));
        Assert.Equal(0m, december.Period.Income);

        var quarter = await SummaryAsync(seed,
            new FinancialSummaryQuery(new DateOnly(2025, 11, 1), new DateOnly(2026, 1, 31)));
        Assert.Equal(3, quarter.Monthly.Count);
        Assert.Equal(253.97m, quarter.Monthly.Last().Income);
        Assert.Equal(quarter.Period.Income, quarter.Monthly.Sum(m => m.Income));
    }

    [Fact]
    public async Task InvalidRanges_AreRejected()
    {
        var seed = await PaymentTestData.SeedAsync();

        await Assert.ThrowsAsync<DomainException>(() => SummaryAsync(seed,
            new FinancialSummaryQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1))));
        await Assert.ThrowsAsync<DomainException>(() => SummaryAsync(seed,
            new FinancialSummaryQuery(new DateOnly(2023, 1, 1), new DateOnly(2026, 1, 15))));
    }

    [Fact]
    public async Task YearComparison_ComparesLikeForLike_AndShowsLastYearsFullTotal()
    {
        var seed = await PaymentTestData.SeedAsync();

        await IssueInvoiceAtAsync(seed, On(2025, 1, 15));   // inside the same period last year
        await IssueInvoiceAtAsync(seed, On(2025, 12, 20));  // later in 2025, outside the same period
        await PaymentTestData.IssueInvoiceAsync(seed);      // 15 Jan 2026
        await PaymentTestData.IssueInvoiceAsync(seed);      // 15 Jan 2026

        var result = await ComparisonAsync(seed, null);

        Assert.Equal(2026, result.Year);
        Assert.Equal(new DateOnly(2026, 1, 15), result.Current.To);
        Assert.Equal(new DateOnly(2025, 1, 15), result.PreviousSamePeriod.To);

        Assert.Equal(507.94m, result.Current.Income);
        Assert.Equal(253.97m, result.PreviousSamePeriod.Income);
        Assert.Equal(507.94m, result.PreviousFullYear.Income);

        Assert.Equal(253.97m, result.Income.Change);
        Assert.Equal(100.0m, result.Income.ChangePercent);
    }

    [Fact]
    public async Task YearComparison_RejectsYearsOutOfRange()
    {
        var seed = await PaymentTestData.SeedAsync();

        await Assert.ThrowsAsync<DomainException>(() => ComparisonAsync(seed, 2027));
        await Assert.ThrowsAsync<DomainException>(() => ComparisonAsync(seed, 1999));
    }

    [Fact]
    public async Task FinancialReports_NeverIncludeAnotherOrganizationsNumbers()
    {
        var seedA = await PaymentTestData.SeedAsync();
        var seedB = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seedA);
        await PaymentTestData.RecordAsync(seedA, invoice.Id, 100.00m);
        await PayableTestData.OpenBillAsync(seedA);

        var p = (await SummaryAsync(seedB, new FinancialSummaryQuery(null, null))).Period;
        Assert.Equal(0m, p.Income);
        Assert.Equal(0m, p.Expenses);
        Assert.Equal(0m, p.CashReceived);
        Assert.Equal(0m, p.NetCashFlow);

        var comparison = await ComparisonAsync(seedB, null);
        Assert.Equal(0m, comparison.Current.Income);
        Assert.Null(comparison.Income.ChangePercent);
    }
}

public class DashboardNetFigureTests
{
    [Fact]
    public async Task Dashboard_SeparatesNetAndTax_WhileKeepingTheGrossTotal()
    {
        var clock = PaymentTestData.Clock;
        var seed = await PaymentTestData.SeedAsync();
        await PaymentTestData.IssueInvoiceAsync(seed); // 273.02 gross = 253.97 net + 19.05 tax

        await using var ctx = TestDatabase.CreateContext(clock, seed.OrganizationId, seed.UserId);
        var handler = new GetDashboardHandler(
            new DashboardRepository(ctx), new ReceivablesRepository(ctx), new OrganizationRepository(ctx),
            new TestCurrentOrganization(seed.OrganizationId), clock);

        var d = await handler.Handle(new DashboardQuery(null, null), CancellationToken.None);

        Assert.Equal(273.02m, d.TotalInvoiced);
        Assert.Equal(253.97m, d.NetInvoiced);
        Assert.Equal(19.05m, d.TaxInvoiced);
        Assert.Equal(d.NetInvoiced + d.TaxInvoiced, d.TotalInvoiced);
    }
}