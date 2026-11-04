using Application.Invoices;
using Application.Reports;
using Domain.Common;
using Infrastructure.Persistence.Repositories;

namespace Tests;

public class DashboardMathTests
{
    [Fact]
    public void Months_ListsEveryMonthInTheRange_AcrossYearEnds()
    {
        var months = DashboardMath.Months(new DateOnly(2025, 11, 20), new DateOnly(2026, 2, 3));

        Assert.Equal(["2025-11", "2025-12", "2026-01", "2026-02"], months);
    }

    [Fact]
    public void DefaultFrom_CoversSixMonthsIncludingTheCurrentOne()
    {
        var from = DashboardMath.DefaultFrom(new DateOnly(2026, 1, 15));

        Assert.Equal(new DateOnly(2025, 8, 1), from);
        Assert.Equal(6, DashboardMath.Months(from, new DateOnly(2026, 1, 15)).Count);
    }
}

public class DashboardTests
{
    private static readonly FixedTimeProvider Clock = PaymentTestData.Clock;

    private static async Task<DashboardDto> RunAsync(
        Seed seed, DateOnly? from = null, DateOnly? to = null, TimeProvider? clock = null)
    {
        clock ??= Clock;
        await using var ctx = TestDatabase.CreateContext(clock, seed.OrganizationId, seed.UserId);
        var handler = new GetDashboardHandler(
            new DashboardRepository(ctx), new ReceivablesRepository(ctx), new OrganizationRepository(ctx),
            new TestCurrentOrganization(seed.OrganizationId), clock);
        return await handler.Handle(new DashboardQuery(from, to), CancellationToken.None);
    }

    [Fact]
    public async Task Dashboard_ShowsInvoicedCollectedAndOutstanding()
    {
        var seed = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed); // 273.02
        await PaymentTestData.RecordAsync(seed, invoice.Id, 100.00m);

        var d = await RunAsync(seed);

        Assert.Equal("NGN", d.CurrencyCode);
        Assert.Equal(273.02m, d.TotalInvoiced);
        Assert.Equal(1, d.InvoiceCount);
        Assert.Equal(100.00m, d.TotalCollected);
        Assert.Equal(1, d.PaymentCount);
        Assert.Equal(173.02m, d.OutstandingReceivables);
        Assert.Equal(1, d.OpenInvoices);
        Assert.Equal(0m, d.OverdueReceivables);
        Assert.Equal(1, d.InvoiceStatuses.PartiallyPaid);

        Assert.Equal(6, d.Monthly.Count);
        var january = d.Monthly.Last();
        Assert.Equal("2026-01", january.Month);
        Assert.Equal(273.02m, january.Invoiced);
        Assert.Equal(100.00m, january.Collected);
        Assert.Equal(0m, d.Monthly.First().Invoiced);

        var top = Assert.Single(d.TopCustomers);
        Assert.Equal(173.02m, top.Outstanding);
        Assert.NotEmpty(d.Definitions);
    }

    [Fact]
    public async Task ReversedPayments_AreNotCountedAsCollected()
    {
        var seed = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed);
        var payment = await PaymentTestData.RecordAsync(seed, invoice.Id, 100.00m);
        await PaymentTestData.ReverseAsync(seed, payment.Id, "Bounced");

        var d = await RunAsync(seed);

        Assert.Equal(0m, d.TotalCollected);
        Assert.Equal(0, d.PaymentCount);
        Assert.Equal(273.02m, d.OutstandingReceivables);
    }

    [Fact]
    public async Task VoidedInvoices_AreNotCountedAsInvoicedOrOutstanding()
    {
        var seed = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed);

        await using (var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId))
        {
            await new InvoiceKit(ctx, seed.OrganizationId, Clock).Void().Handle(
                invoice.Id, new VoidInvoiceCommand("Raised in error"), CancellationToken.None);
        }

        var d = await RunAsync(seed);

        Assert.Equal(0m, d.TotalInvoiced);
        Assert.Equal(0m, d.OutstandingReceivables);
        Assert.Equal(1, d.InvoiceStatuses.Void);
    }

    [Fact]
    public async Task OverdueReceivables_AppearOnceTheDueDatePasses()
    {
        var seed = await PaymentTestData.SeedAsync();
        await PaymentTestData.IssueInvoiceAsync(seed); // due 14 Feb 2026

        var later = new FixedTimeProvider(Clock.GetUtcNow().AddDays(60));
        var d = await RunAsync(seed, clock: later);

        Assert.Equal(273.02m, d.OutstandingReceivables);
        Assert.Equal(273.02m, d.OverdueReceivables);
        Assert.Equal(1, d.OverdueInvoices);
    }

    [Fact]
    public async Task TheDateRange_FiltersFlowsButNotTheOutstandingSnapshot()
    {
        var seed = await PaymentTestData.SeedAsync();
        await PaymentTestData.IssueInvoiceAsync(seed); // issued 15 Jan 2026

        var d = await RunAsync(seed, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));

        Assert.Equal(0m, d.TotalInvoiced);
        Assert.Equal(0, d.InvoiceCount);
        Assert.Equal(273.02m, d.OutstandingReceivables);
        Assert.Equal(12, d.Monthly.Count);
    }

    [Fact]
    public async Task InvalidDateRanges_AreRejected()
    {
        var seed = await PaymentTestData.SeedAsync();

        await Assert.ThrowsAsync<DomainException>(
            () => RunAsync(seed, new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1)));
        await Assert.ThrowsAsync<DomainException>(
            () => RunAsync(seed, new DateOnly(2023, 1, 1), new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public async Task Dashboard_NeverIncludesAnotherOrganizationsNumbers()
    {
        var seedA = await PaymentTestData.SeedAsync();
        var seedB = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seedA);
        await PaymentTestData.RecordAsync(seedA, invoice.Id, 100.00m);

        var d = await RunAsync(seedB);

        Assert.Equal(0m, d.TotalInvoiced);
        Assert.Equal(0m, d.TotalCollected);
        Assert.Equal(0m, d.OutstandingReceivables);
        Assert.Empty(d.TopCustomers);
        Assert.Equal(0, d.InvoiceStatuses.Issued + d.InvoiceStatuses.PartiallyPaid + d.InvoiceStatuses.Paid);
    }
}