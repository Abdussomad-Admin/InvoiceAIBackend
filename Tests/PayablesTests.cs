using Application.Bills;
using Application.Common.Exceptions;
using Application.Reports;
using Application.Suppliers;
using Domain.Bills;
using Domain.Common;
using Domain.Payments;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public class BillPaymentEntityTests
{
    private static BillPayment New(decimal amount = 50m, PaymentMethod method = PaymentMethod.BankTransfer)
        => BillPayment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ngn", amount,
            new DateOnly(2026, 1, 15), method, "  REF-1  ", null, Guid.NewGuid());

    [Fact]
    public void Create_CleansTheInput()
    {
        var payment = New();

        Assert.Equal("NGN", payment.CurrencyCode);
        Assert.Equal("REF-1", payment.Reference);
        Assert.False(payment.IsReversed);
    }

    [Fact]
    public void Create_WithInvalidValues_Throws()
    {
        Assert.Throws<DomainException>(() => New(amount: 0m));
        Assert.Throws<DomainException>(() => New(amount: -5m));
        Assert.Throws<DomainException>(() => New(amount: 10.005m));
        Assert.Throws<DomainException>(() => New(method: (PaymentMethod)99));
        Assert.Throws<DomainException>(() => BillPayment.Create(
            Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "NGN", 5m,
            new DateOnly(2026, 1, 15), PaymentMethod.Cash, null, null, Guid.NewGuid()));
    }

    [Fact]
    public void Reverse_NeedsAReason_AndWorksOnlyOnce()
    {
        var payment = New();
        var now = new DateTimeOffset(2026, 1, 20, 9, 0, 0, TimeSpan.Zero);

        Assert.Throws<DomainException>(() => payment.Reverse("  ", Guid.NewGuid(), now));

        payment.Reverse("Sent to the wrong account", Guid.NewGuid(), now);
        Assert.True(payment.IsReversed);

        Assert.Throws<DomainException>(() => payment.Reverse("Again", Guid.NewGuid(), now));
    }
}

internal sealed class BillPaymentKit(AppDbContext ctx, Seed seed, TimeProvider clock)
{
    private readonly TestCurrentOrganization _organization = new(seed.OrganizationId);
    private readonly TestCurrentUser _user = new(seed.UserId);

    public RecordBillPaymentHandler Record() => new(
        new BillRepository(ctx), new BillPaymentRepository(ctx), new SupplierRepository(ctx),
        new OrganizationRepository(ctx), ctx, ctx, _organization, _user, clock);

    public ReverseBillPaymentHandler Reverse() => new(
        new BillRepository(ctx), new BillPaymentRepository(ctx), new SupplierRepository(ctx),
        ctx, ctx, _user, clock);

    public ListBillPaymentsHandler List() => new(new BillPaymentRepository(ctx));

    public GetSupplierSummaryHandler Summary() => new(
        new SupplierRepository(ctx), new PayablesRepository(ctx), new OrganizationRepository(ctx),
        _organization, clock);

    public GetPayablesAgingHandler Aging() => new(
        new PayablesRepository(ctx), new OrganizationRepository(ctx), _organization, clock);

    public GetExpenseReportHandler Expenses() => new(
        new PayablesRepository(ctx), new OrganizationRepository(ctx), _organization, clock);
}

internal static class PayableTestData
{
    public static readonly FixedTimeProvider Clock = PaymentTestData.Clock; // 15 January 2026

    // An OPEN bill of 1075.00 (1000.00 + 75.00 tax), dated 10 Jan 2026, due 9 Feb 2026.
    public static async Task<(SupplierDto Supplier, BillDto Bill)> OpenBillAsync(
        Seed seed, SupplierDto? supplier = null, string number = "SUP-001")
    {
        supplier ??= await BillTestData.SupplierAsync(seed);
        var draft = await BillTestData.BillAsync(seed, supplier.Id, number);
        var open = await BillTestData.FinalizeAsync(seed, draft.Id);
        return (supplier, open);
    }

    public static async Task<BillPaymentDto> PayAsync(
        Seed seed, Guid billId, decimal amount, string? reference = null, DateOnly? paidOn = null)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        return await new BillPaymentKit(ctx, seed, Clock).Record().Handle(
            billId, new RecordBillPaymentCommand(amount, paidOn, PaymentMethod.BankTransfer, reference, null),
            CancellationToken.None);
    }

    public static async Task<BillPaymentDto> ReverseAsync(Seed seed, Guid paymentId, string reason)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        return await new BillPaymentKit(ctx, seed, Clock).Reverse().Handle(
            paymentId, new ReverseBillPaymentCommand(reason), CancellationToken.None);
    }

    public static async Task<IReadOnlyList<BillPaymentDto>> HistoryAsync(Seed seed, Guid billId)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var result = await new BillPaymentKit(ctx, seed, Clock).List().Handle(
            new ListBillPaymentsQuery(billId, null, null, null, true, 1, 100), CancellationToken.None);
        return result.Items;
    }
}

public class BillPaymentFlowTests
{
    private static readonly FixedTimeProvider Clock = PayableTestData.Clock;

    [Fact]
    public async Task PartialThenFinalPayment_UpdatesTheBalanceAndStatus()
    {
        var seed = await PaymentTestData.SeedAsync();
        var (_, bill) = await PayableTestData.OpenBillAsync(seed);

        await PayableTestData.PayAsync(seed, bill.Id, 500.00m);
        var afterFirst = await BillTestData.GetAsync(seed, bill.Id);
        Assert.Equal(BillStatus.PartiallyPaid, afterFirst.Status);
        Assert.Equal(575.00m, afterFirst.Balance);

        await PayableTestData.PayAsync(seed, bill.Id, 575.00m);
        var afterSecond = await BillTestData.GetAsync(seed, bill.Id);
        Assert.Equal(BillStatus.Paid, afterSecond.Status);
        Assert.Equal(0m, afterSecond.Balance);

        var history = await PayableTestData.HistoryAsync(seed, bill.Id);
        Assert.Equal(2, history.Count);
        Assert.Equal(1075.00m, history.Sum(p => p.Amount));
        Assert.All(history, p => Assert.Equal("SUP-001", p.BillNumber));
    }

    [Fact]
    public async Task PaymentMoreThanTheBalance_IsRejected_AndNothingIsSaved()
    {
        var seed = await PaymentTestData.SeedAsync();
        var (_, bill) = await PayableTestData.OpenBillAsync(seed);

        await Assert.ThrowsAsync<DomainException>(() => PayableTestData.PayAsync(seed, bill.Id, 1075.01m));

        var read = await BillTestData.GetAsync(seed, bill.Id);
        Assert.Equal(0m, read.AmountPaid);
        Assert.Empty(await PayableTestData.HistoryAsync(seed, bill.Id));
    }

    [Fact]
    public async Task PaymentOnADraftBill_IsRejected()
    {
        var seed = await PaymentTestData.SeedAsync();
        var supplier = await BillTestData.SupplierAsync(seed);
        var draft = await BillTestData.BillAsync(seed, supplier.Id);

        await Assert.ThrowsAsync<DomainException>(() => PayableTestData.PayAsync(seed, draft.Id, 10m));
    }

    [Fact]
    public async Task PaymentDatedInTheFuture_IsRejected()
    {
        var seed = await PaymentTestData.SeedAsync();
        var (_, bill) = await PayableTestData.OpenBillAsync(seed);

        await Assert.ThrowsAsync<DomainException>(() =>
            PayableTestData.PayAsync(seed, bill.Id, 10m, paidOn: new DateOnly(2026, 1, 16)));
    }

    [Fact]
    public async Task ReversingAPayment_RestoresTheBalance_AndKeepsTheHistory()
    {
        var seed = await PaymentTestData.SeedAsync();
        var (_, bill) = await PayableTestData.OpenBillAsync(seed);
        var payment = await PayableTestData.PayAsync(seed, bill.Id, 1075.00m);
        Assert.Equal(BillStatus.Paid, (await BillTestData.GetAsync(seed, bill.Id)).Status);

        await PayableTestData.ReverseAsync(seed, payment.Id, "Sent to the wrong account");

        var read = await BillTestData.GetAsync(seed, bill.Id);
        Assert.Equal(BillStatus.Open, read.Status);
        Assert.Equal(1075.00m, read.Balance);

        var kept = Assert.Single(await PayableTestData.HistoryAsync(seed, bill.Id));
        Assert.True(kept.IsReversed);
        Assert.Equal("Sent to the wrong account", kept.ReversalReason);

        await Assert.ThrowsAsync<DomainException>(() => PayableTestData.ReverseAsync(seed, payment.Id, "Again"));
    }

    [Fact]
    public async Task ADuplicateReference_IsRejected_UntilTheFirstPaymentIsReversed()
    {
        var seed = await PaymentTestData.SeedAsync();
        var (_, bill) = await PayableTestData.OpenBillAsync(seed);

        var first = await PayableTestData.PayAsync(seed, bill.Id, 100m, reference: "TRF-1");

        await Assert.ThrowsAsync<ConflictException>(
            () => PayableTestData.PayAsync(seed, bill.Id, 100m, reference: "TRF-1"));

        await PayableTestData.ReverseAsync(seed, first.Id, "Entered twice");
        await PayableTestData.PayAsync(seed, bill.Id, 100m, reference: "TRF-1");
    }

    [Fact]
    public async Task TwoSimultaneousFullPayments_OnlyOneSucceeds()
    {
        var seed = await PaymentTestData.SeedAsync();
        var (_, bill) = await PayableTestData.OpenBillAsync(seed);

        async Task<bool> TryPayAsync()
        {
            try
            {
                await PayableTestData.PayAsync(seed, bill.Id, 1075.00m);
                return true;
            }
            catch (DomainException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(TryPayAsync(), TryPayAsync());

        Assert.Single(results, succeeded => succeeded);

        var read = await BillTestData.GetAsync(seed, bill.Id);
        Assert.Equal(1075.00m, read.AmountPaid);
        Assert.Equal(BillStatus.Paid, read.Status);
        Assert.Single(await PayableTestData.HistoryAsync(seed, bill.Id));
    }

    [Fact]
    public async Task AnotherOrganization_CannotPayReverseOrSeeThePayments()
    {
        var seedA = await PaymentTestData.SeedAsync();
        var seedB = await PaymentTestData.SeedAsync();
        var (_, bill) = await PayableTestData.OpenBillAsync(seedA);
        var payment = await PayableTestData.PayAsync(seedA, bill.Id, 20m);

        await Assert.ThrowsAsync<NotFoundException>(() => PayableTestData.PayAsync(seedB, bill.Id, 20m));
        await Assert.ThrowsAsync<NotFoundException>(() => PayableTestData.ReverseAsync(seedB, payment.Id, "Sneaky"));
        Assert.Empty(await PayableTestData.HistoryAsync(seedB, bill.Id));
    }

    [Fact]
    public async Task PaymentsToSuppliers_AreAudited()
    {
        var seed = await PaymentTestData.SeedAsync();
        var (_, bill) = await PayableTestData.OpenBillAsync(seed);
        var payment = await PayableTestData.PayAsync(seed, bill.Id, 100.00m);
        await PayableTestData.ReverseAsync(seed, payment.Id, "Wrong account");

        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var trail = await ctx.AuditEntries.ToListAsync();

        Assert.Contains(trail, a => a.Action == "BillPayment.Recorded" && a.EntityId == payment.Id);
        Assert.Contains(trail, a => a.Action == "BillPayment.Reversed" && a.EntityId == payment.Id);
        Assert.Contains(trail, a =>
            a.Action == "Bill.PaymentChanged" && a.EntityId == bill.Id &&
            a.Details!.StartsWith("Amount paid 0.00 -> 100.00"));
    }
}

public class PayablesReportTests
{
    private static readonly FixedTimeProvider Clock = PayableTestData.Clock;

    [Fact]
    public async Task SupplierSummary_ShowsBilledPaidOutstanding_AndOverdueOnceDue()
    {
        var seed = await PaymentTestData.SeedAsync();
        var (supplier, bill) = await PayableTestData.OpenBillAsync(seed); // 1075.00, due 9 Feb 2026
        await PayableTestData.PayAsync(seed, bill.Id, 500.00m);

        await using (var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId))
        {
            var summary = await new BillPaymentKit(ctx, seed, Clock).Summary().Handle(supplier.Id, CancellationToken.None);

            Assert.Equal(1075.00m, summary.TotalBilled);
            Assert.Equal(500.00m, summary.TotalPaid);
            Assert.Equal(575.00m, summary.Outstanding);
            Assert.Equal(0m, summary.Overdue);
            Assert.Equal(1, summary.OpenBills);
        }

        var later = new FixedTimeProvider(Clock.GetUtcNow().AddDays(60)); // 16 March 2026
        await using (var ctx = TestDatabase.CreateContext(later, seed.OrganizationId, seed.UserId))
        {
            var summary = await new BillPaymentKit(ctx, seed, later).Summary().Handle(supplier.Id, CancellationToken.None);

            Assert.Equal(575.00m, summary.Overdue);
            Assert.Equal(1, summary.OverdueBills);
        }
    }

    [Fact]
    public async Task PayablesAging_UsesBucketsAndShowsUpcomingObligations()
    {
        var seed = await PaymentTestData.SeedAsync();
        var (supplier, bill) = await PayableTestData.OpenBillAsync(seed); // 1075.00, due 9 Feb 2026

        // A second bill of 200.00, due 20 Jan 2026 (5 days after the fixed "today" of 15 Jan).
        var soon = await BillTestData.BillAsync(
            seed, supplier.Id, "SUP-002", billDate: new DateOnly(2026, 1, 11),
            dueDate: new DateOnly(2026, 1, 20), lines: [new BillLineInput("Other", 1m, 200m)]);
        await BillTestData.FinalizeAsync(seed, soon.Id);

        await using (var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId))
        {
            var report = await new BillPaymentKit(ctx, seed, Clock).Aging().Handle(CancellationToken.None);

            Assert.Equal(1275.00m, report.TotalOutstanding);
            Assert.Equal(0m, report.TotalOverdue);
            Assert.Equal(200.00m, report.DueWithin7Days);
            Assert.Equal(1275.00m, report.DueWithin30Days);

            var current = report.Buckets.Single(b => b.Bucket == AgingBucket.Current);
            Assert.Equal(2, current.InvoiceCount);
            Assert.Equal(1275.00m, current.Balance);
        }

        await PayableTestData.PayAsync(seed, bill.Id, 500.00m);

        var later = new FixedTimeProvider(Clock.GetUtcNow().AddDays(60)); // 16 March 2026
        await using (var ctx = TestDatabase.CreateContext(later, seed.OrganizationId, seed.UserId))
        {
            var report = await new BillPaymentKit(ctx, seed, later).Aging().Handle(CancellationToken.None);

            // 9 Feb is 35 days overdue; 20 Jan is 55 days overdue: both in the 31-60 bucket.
            var bucket = report.Buckets.Single(b => b.Bucket == AgingBucket.Days31To60);
            Assert.Equal(2, bucket.InvoiceCount);
            Assert.Equal(775.00m, bucket.Balance);
            Assert.Equal(775.00m, report.TotalOverdue);

            var row = Assert.Single(report.Suppliers);
            Assert.Equal(775.00m, row.Days31To60);
        }
    }

    [Fact]
    public async Task ExpenseReport_SplitsNetTaxAndGross_AndCountsCashSeparately()
    {
        var seed = await PaymentTestData.SeedAsync();
        var (supplier, counted) = await PayableTestData.OpenBillAsync(seed); // 1000.00 net + 75.00 tax

        // A draft and a voided bill must not count.
        await BillTestData.BillAsync(seed, supplier.Id, "SUP-002", lines: [new BillLineInput("Draft", 1m, 200m)]);
        var toVoid = await BillTestData.BillAsync(seed, supplier.Id, "SUP-003", lines: [new BillLineInput("Void", 1m, 300m)]);
        await BillTestData.FinalizeAsync(seed, toVoid.Id);
        await BillTestData.VoidAsync(seed, toVoid.Id);

        // 500.00 paid and kept; 100.00 paid and then reversed (must not count).
        await PayableTestData.PayAsync(seed, counted.Id, 500.00m);
        var reversed = await PayableTestData.PayAsync(seed, counted.Id, 100.00m);
        await PayableTestData.ReverseAsync(seed, reversed.Id, "Wrong account");

        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var report = await new BillPaymentKit(ctx, seed, Clock).Expenses().Handle(
            new ExpenseQuery(null, null), CancellationToken.None);

        Assert.Equal(new DateOnly(2025, 11, 1), report.From);
        Assert.Equal(new DateOnly(2026, 1, 15), report.To);
        Assert.Equal(1000.00m, report.NetExpenses);
        Assert.Equal(75.00m, report.TaxOnExpenses);
        Assert.Equal(1075.00m, report.GrossBilled);
        Assert.Equal(report.NetExpenses + report.TaxOnExpenses, report.GrossBilled);
        Assert.Equal(1, report.BillCount);
        Assert.Equal(500.00m, report.CashPaid);
        Assert.Equal(1, report.PaymentCount);

        Assert.Equal(3, report.Monthly.Count);
        var january = report.Monthly.Last();
        Assert.Equal("2026-01", january.Month);
        Assert.Equal(1000.00m, january.NetBilled);
        Assert.Equal(500.00m, january.Paid);

        var top = Assert.Single(report.TopSuppliers);
        Assert.Equal(1000.00m, top.NetBilled);
        Assert.NotEmpty(report.Definitions);
    }

    [Fact]
    public async Task ExpenseReport_RespectsTheDateRange_AndRejectsInvalidOnes()
    {
        var seed = await PaymentTestData.SeedAsync();
        await PayableTestData.OpenBillAsync(seed); // dated 10 Jan 2026

        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var handler = new BillPaymentKit(ctx, seed, Clock).Expenses();

        var lastYear = await handler.Handle(
            new ExpenseQuery(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)), CancellationToken.None);
        Assert.Equal(0m, lastYear.NetExpenses);
        Assert.Equal(12, lastYear.Monthly.Count);

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new ExpenseQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1)), CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new ExpenseQuery(new DateOnly(2023, 1, 1), new DateOnly(2026, 1, 15)), CancellationToken.None));
    }

    [Fact]
    public async Task PayablesReports_NeverIncludeAnotherOrganizationsNumbers()
    {
        var seedA = await PaymentTestData.SeedAsync();
        var seedB = await PaymentTestData.SeedAsync();
        var (supplier, bill) = await PayableTestData.OpenBillAsync(seedA);
        await PayableTestData.PayAsync(seedA, bill.Id, 500.00m);

        await using var ctx = TestDatabase.CreateContext(Clock, seedB.OrganizationId, seedB.UserId);
        var kit = new BillPaymentKit(ctx, seedB, Clock);

        var aging = await kit.Aging().Handle(CancellationToken.None);
        Assert.Equal(0m, aging.TotalOutstanding);
        Assert.Empty(aging.Suppliers);

        var expenses = await kit.Expenses().Handle(new ExpenseQuery(null, null), CancellationToken.None);
        Assert.Equal(0m, expenses.NetExpenses);
        Assert.Equal(0m, expenses.CashPaid);
        Assert.Empty(expenses.TopSuppliers);

        await Assert.ThrowsAsync<NotFoundException>(() => kit.Summary().Handle(supplier.Id, CancellationToken.None));
    }
}