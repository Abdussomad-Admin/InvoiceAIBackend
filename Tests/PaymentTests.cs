using Application.Common.Exceptions;
using Application.Invoices;
using Application.Payments;
using Application.Reports;
using Domain.Common;
using Domain.Customers;
using Domain.Invoices;
using Domain.Organizations;
using Domain.Payments;
using Domain.Users;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;

namespace Tests;

internal sealed record Seed(Guid OrganizationId, Guid CustomerId, Guid UserId);

internal sealed class PaymentKit(AppDbContext ctx, Seed seed, TimeProvider clock)
{
    private readonly TestCurrentOrganization _organization = new(seed.OrganizationId);
    private readonly TestCurrentUser _user = new(seed.UserId);

    public RecordPaymentHandler Record() => new(
        new InvoiceRepository(ctx), new PaymentRepository(ctx), new CustomerRepository(ctx),
        new OrganizationRepository(ctx), ctx, ctx, _organization, _user, clock);

    public ReversePaymentHandler Reverse() => new(
        new InvoiceRepository(ctx), new PaymentRepository(ctx), new CustomerRepository(ctx),
        ctx, ctx, _user, clock);

    public ListPaymentsHandler List() => new(new PaymentRepository(ctx));

    public GetCustomerSummaryHandler Summary() => new(
        new CustomerRepository(ctx), new ReceivablesRepository(ctx), new OrganizationRepository(ctx),
        _organization, clock);

    public GetAgingHandler Aging() => new(
        new ReceivablesRepository(ctx), new OrganizationRepository(ctx), _organization, clock);
}

internal static class PaymentTestData
{
    public static readonly FixedTimeProvider Clock =
        new(new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero));

    public static async Task<Seed> SeedAsync()
    {
        await using var ctx = TestDatabase.CreateContext(Clock);
        var organization = Organization.Create($"Org {Guid.NewGuid()}", "NG", "NGN", "Africa/Lagos");
        var user = User.Register($"{Guid.NewGuid()}@example.com", "hash", "Tester");
        var customer = Customer.Create(organization.Id, $"Customer {Guid.NewGuid()}");
        ctx.Organizations.Add(organization);
        ctx.Users.Add(user);
        ctx.Customers.Add(customer);
        await ctx.SaveChangesAsync();
        return new Seed(organization.Id, customer.Id, user.Id);
    }

    // Total 273.02
    public static IReadOnlyList<InvoiceLineInput> Lines { get; } =
    [
        new InvoiceLineInput("Widget", 3m, 19.99m, 10m, 7.5m),
        new InvoiceLineInput("Service", 2m, 100m, 0m, 7.5m)
    ];

    public static async Task<InvoiceDto> CreateDraftAsync(Seed seed)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        return await new InvoiceKit(ctx, seed.OrganizationId, Clock).Create().Handle(
            new InvoiceInput(seed.CustomerId, null, null, Lines), CancellationToken.None);
    }

    public static async Task<InvoiceDto> IssueInvoiceAsync(Seed seed)
    {
        var draft = await CreateDraftAsync(seed);
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        return await new InvoiceKit(ctx, seed.OrganizationId, Clock).Issue().Handle(
            draft.Id, null, CancellationToken.None);
    }

    public static async Task<InvoiceDto> GetInvoiceAsync(Seed seed, Guid invoiceId)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        return await new InvoiceKit(ctx, seed.OrganizationId, Clock).Get().Handle(invoiceId, CancellationToken.None);
    }

    public static async Task<PaymentDto> RecordAsync(
        Seed seed, Guid invoiceId, decimal amount, string? reference = null, DateOnly? paidOn = null)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        return await new PaymentKit(ctx, seed, Clock).Record().Handle(
            invoiceId, new RecordPaymentCommand(amount, paidOn, PaymentMethod.BankTransfer, reference, null),
            CancellationToken.None);
    }

    public static async Task<PaymentDto> ReverseAsync(Seed seed, Guid paymentId, string reason)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        return await new PaymentKit(ctx, seed, Clock).Reverse().Handle(
            paymentId, new ReversePaymentCommand(reason), CancellationToken.None);
    }

    public static async Task<IReadOnlyList<PaymentDto>> HistoryAsync(Seed seed, Guid invoiceId)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var result = await new PaymentKit(ctx, seed, Clock).List().Handle(
            new ListPaymentsQuery(invoiceId, null, null, null, true, 1, 100), CancellationToken.None);
        return result.Items;
    }
}

public class PaymentEntityTests
{
    private static Payment New(decimal amount = 50m, PaymentMethod method = PaymentMethod.Cash)
        => Payment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ngn", amount,
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
        Assert.Throws<DomainException>(() => Payment.Create(
            Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "NGN", 5m,
            new DateOnly(2026, 1, 15), PaymentMethod.Cash, null, null, Guid.NewGuid()));
    }

    [Fact]
    public void Reverse_NeedsAReason_AndWorksOnlyOnce()
    {
        var payment = New();
        var now = new DateTimeOffset(2026, 1, 20, 9, 0, 0, TimeSpan.Zero);

        Assert.Throws<DomainException>(() => payment.Reverse("  ", Guid.NewGuid(), now));

        payment.Reverse("Bounced", Guid.NewGuid(), now);
        Assert.True(payment.IsReversed);
        Assert.Equal("Bounced", payment.ReversalReason);

        Assert.Throws<DomainException>(() => payment.Reverse("Again", Guid.NewGuid(), now));
    }
}

public class AgingTests
{
    [Theory]
    [InlineData(-5, AgingBucket.Current)]
    [InlineData(0, AgingBucket.Current)]
    [InlineData(1, AgingBucket.Days1To30)]
    [InlineData(30, AgingBucket.Days1To30)]
    [InlineData(31, AgingBucket.Days31To60)]
    [InlineData(60, AgingBucket.Days31To60)]
    [InlineData(61, AgingBucket.Days61To90)]
    [InlineData(90, AgingBucket.Days61To90)]
    [InlineData(91, AgingBucket.Over90)]
    public void Classify_PutsInvoicesInTheRightBucket(int daysOverdue, AgingBucket expected)
    {
        var today = new DateOnly(2026, 6, 30);
        var due = today.AddDays(-daysOverdue);

        Assert.Equal(expected, Aging.Classify(due, today));
    }
}

public class PaymentFlowTests
{
    private static readonly FixedTimeProvider Clock = PaymentTestData.Clock;

    [Fact]
    public async Task PartialThenFinalPayment_UpdatesTheBalanceAndStatus()
    {
        var seed = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed);

        await PaymentTestData.RecordAsync(seed, invoice.Id, 100.00m);
        var afterFirst = await PaymentTestData.GetInvoiceAsync(seed, invoice.Id);
        Assert.Equal(InvoiceStatus.PartiallyPaid, afterFirst.Status);
        Assert.Equal(173.02m, afterFirst.Balance);

        await PaymentTestData.RecordAsync(seed, invoice.Id, 173.02m);
        var afterSecond = await PaymentTestData.GetInvoiceAsync(seed, invoice.Id);
        Assert.Equal(InvoiceStatus.Paid, afterSecond.Status);
        Assert.Equal(0m, afterSecond.Balance);

        var history = await PaymentTestData.HistoryAsync(seed, invoice.Id);
        Assert.Equal(2, history.Count);
        Assert.Equal(273.02m, history.Sum(p => p.Amount));
        Assert.All(history, p => Assert.Equal("INV-0001", p.InvoiceNumber));
    }

    [Fact]
    public async Task PaymentMoreThanTheBalance_IsRejected_AndNothingIsSaved()
    {
        var seed = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed);

        await Assert.ThrowsAsync<DomainException>(() => PaymentTestData.RecordAsync(seed, invoice.Id, 273.03m));

        var read = await PaymentTestData.GetInvoiceAsync(seed, invoice.Id);
        Assert.Equal(0m, read.AmountPaid);
        Assert.Empty(await PaymentTestData.HistoryAsync(seed, invoice.Id));
    }

    [Fact]
    public async Task PaymentOnADraft_IsRejected()
    {
        var seed = await PaymentTestData.SeedAsync();
        var draft = await PaymentTestData.CreateDraftAsync(seed);

        await Assert.ThrowsAsync<DomainException>(() => PaymentTestData.RecordAsync(seed, draft.Id, 10m));
    }

    [Fact]
    public async Task PaymentDatedInTheFuture_IsRejected()
    {
        var seed = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed);

        await Assert.ThrowsAsync<DomainException>(() =>
            PaymentTestData.RecordAsync(seed, invoice.Id, 10m, paidOn: new DateOnly(2026, 1, 16)));
    }

    [Fact]
    public async Task ReversingAPayment_RestoresTheBalance_AndKeepsTheHistory()
    {
        var seed = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed);
        var payment = await PaymentTestData.RecordAsync(seed, invoice.Id, 273.02m);
        Assert.Equal(InvoiceStatus.Paid, (await PaymentTestData.GetInvoiceAsync(seed, invoice.Id)).Status);

        await PaymentTestData.ReverseAsync(seed, payment.Id, "Bounced");

        var read = await PaymentTestData.GetInvoiceAsync(seed, invoice.Id);
        Assert.Equal(InvoiceStatus.Issued, read.Status);
        Assert.Equal(273.02m, read.Balance);

        var history = await PaymentTestData.HistoryAsync(seed, invoice.Id);
        var kept = Assert.Single(history);
        Assert.True(kept.IsReversed);
        Assert.Equal("Bounced", kept.ReversalReason);

        await Assert.ThrowsAsync<DomainException>(() => PaymentTestData.ReverseAsync(seed, payment.Id, "Again"));
    }

    [Fact]
    public async Task ADuplicateReference_IsRejected_UntilTheFirstPaymentIsReversed()
    {
        var seed = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed);

        var first = await PaymentTestData.RecordAsync(seed, invoice.Id, 50m, reference: "TRF-1");

        await Assert.ThrowsAsync<ConflictException>(
            () => PaymentTestData.RecordAsync(seed, invoice.Id, 50m, reference: "TRF-1"));

        await PaymentTestData.ReverseAsync(seed, first.Id, "Entered twice");
        await PaymentTestData.RecordAsync(seed, invoice.Id, 50m, reference: "TRF-1");
    }

    [Fact]
    public async Task TwoSimultaneousFullPayments_OnlyOneSucceeds()
    {
        var seed = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed);

        async Task<bool> TryPayAsync()
        {
            try
            {
                await PaymentTestData.RecordAsync(seed, invoice.Id, 273.02m);
                return true;
            }
            catch (DomainException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(TryPayAsync(), TryPayAsync());

        Assert.Single(results, succeeded => succeeded);

        var read = await PaymentTestData.GetInvoiceAsync(seed, invoice.Id);
        Assert.Equal(273.02m, read.AmountPaid);
        Assert.Equal(InvoiceStatus.Paid, read.Status);
        Assert.Single(await PaymentTestData.HistoryAsync(seed, invoice.Id));
    }

    [Fact]
    public async Task AnotherOrganization_CannotPayReverseOrSeeThePayments()
    {
        var seedA = await PaymentTestData.SeedAsync();
        var seedB = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seedA);
        var payment = await PaymentTestData.RecordAsync(seedA, invoice.Id, 20m);

        await Assert.ThrowsAsync<NotFoundException>(() => PaymentTestData.RecordAsync(seedB, invoice.Id, 20m));
        await Assert.ThrowsAsync<NotFoundException>(() => PaymentTestData.ReverseAsync(seedB, payment.Id, "Sneaky"));
        Assert.Empty(await PaymentTestData.HistoryAsync(seedB, invoice.Id));
    }
}

public class ReceivablesTests
{
    private static readonly FixedTimeProvider Clock = PaymentTestData.Clock;

    [Fact]
    public async Task CustomerSummary_ShowsInvoicedPaidAndOutstanding_AndOverdueOnceDue()
    {
        var seed = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed); // 273.02, due 14 Feb 2026
        await PaymentTestData.RecordAsync(seed, invoice.Id, 100.00m);

        await using (var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId))
        {
            var summary = await new PaymentKit(ctx, seed, Clock).Summary().Handle(seed.CustomerId, CancellationToken.None);

            Assert.Equal(273.02m, summary.TotalInvoiced);
            Assert.Equal(100.00m, summary.TotalPaid);
            Assert.Equal(173.02m, summary.Outstanding);
            Assert.Equal(0m, summary.Overdue);
            Assert.Equal(1, summary.OpenInvoices);
        }

        var later = new FixedTimeProvider(Clock.GetUtcNow().AddDays(60));
        await using (var ctx = TestDatabase.CreateContext(later, seed.OrganizationId, seed.UserId))
        {
            var summary = await new PaymentKit(ctx, seed, later).Summary().Handle(seed.CustomerId, CancellationToken.None);

            Assert.Equal(173.02m, summary.Overdue);
            Assert.Equal(1, summary.OverdueInvoices);
        }
    }

    [Fact]
    public async Task AgingReport_PlacesTheBalanceInTheRightBucket()
    {
        var seed = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed); // due 14 Feb 2026
        await PaymentTestData.RecordAsync(seed, invoice.Id, 100.00m);

        var later = new FixedTimeProvider(Clock.GetUtcNow().AddDays(60)); // 16 March: 30 days overdue
        await using var ctx = TestDatabase.CreateContext(later, seed.OrganizationId, seed.UserId);
        var report = await new PaymentKit(ctx, seed, later).Aging().Handle(CancellationToken.None);

        var bucket = report.Buckets.Single(b => b.Bucket == AgingBucket.Days1To30);
        Assert.Equal(1, bucket.InvoiceCount);
        Assert.Equal(173.02m, bucket.Balance);
        Assert.Equal(173.02m, report.TotalOutstanding);
        Assert.Equal(173.02m, report.TotalOverdue);

        var row = Assert.Single(report.Customers);
        Assert.Equal(173.02m, row.Days1To30);
        Assert.Equal(173.02m, row.Total);
    }
}