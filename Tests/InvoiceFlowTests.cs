using Application.Common;
using Application.Common.Exceptions;
using Application.Invoices;
using Domain.Common;
using Domain.Customers;
using Domain.Invoices;
using Domain.Organizations;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;

namespace Tests;

internal sealed class InvoiceKit(AppDbContext ctx, Guid? organizationId, TimeProvider clock)
{
    private readonly TestCurrentOrganization _organization = new(organizationId);

    public CreateInvoiceHandler Create() => new(
        new InvoiceRepository(ctx), new CustomerRepository(ctx), new OrganizationRepository(ctx),
        ctx, _organization, clock);

    public UpdateInvoiceHandler Update() => new(
        new InvoiceRepository(ctx), new CustomerRepository(ctx), new OrganizationRepository(ctx),
        ctx, _organization, clock);

    public IssueInvoiceHandler Issue() => new(
        new InvoiceRepository(ctx), new CustomerRepository(ctx), new OrganizationRepository(ctx),
        new InvoiceNumberGenerator(ctx), ctx, ctx, _organization, clock);

    public VoidInvoiceHandler Void() => new(
        new InvoiceRepository(ctx), new CustomerRepository(ctx), new OrganizationRepository(ctx),
        ctx, _organization, clock);

    public GetInvoiceHandler Get() => new(
        new InvoiceRepository(ctx), new CustomerRepository(ctx), new OrganizationRepository(ctx),
        _organization, clock);

    public ListInvoicesHandler List() => new(
        new InvoiceRepository(ctx), new OrganizationRepository(ctx), _organization, clock);
}

public class InvoiceFlowTests
{
    private static readonly FixedTimeProvider Clock =
        new(new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero));

    private static IReadOnlyList<InvoiceLineInput> SampleLines() =>
    [
        new InvoiceLineInput("Widget", 3m, 19.99m, 10m, 7.5m),
        new InvoiceLineInput("Service", 2m, 100m, 0m, 7.5m)
    ];

    private static async Task<(Guid OrganizationId, Guid CustomerId)> SeedAsync(bool archivedCustomer = false)
    {
        await using var ctx = TestDatabase.CreateContext(Clock);
        var organization = Organization.Create($"Org {Guid.NewGuid()}", "NG", "NGN", "Africa/Lagos");
        ctx.Organizations.Add(organization);

        var customer = Customer.Create(organization.Id, $"Customer {Guid.NewGuid()}");
        if (archivedCustomer) customer.Archive();
        ctx.Customers.Add(customer);

        await ctx.SaveChangesAsync();
        return (organization.Id, customer.Id);
    }

    private static async Task<InvoiceDto> CreateDraftAsync(
        Guid organizationId, Guid customerId, IReadOnlyList<InvoiceLineInput>? lines = null)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, organizationId);
        return await new InvoiceKit(ctx, organizationId, Clock).Create().Handle(
            new InvoiceInput(customerId, null, "Thank you", lines ?? SampleLines()), CancellationToken.None);
    }

    private static async Task<InvoiceDto> IssueAsync(Guid organizationId, Guid invoiceId, TimeProvider? clock = null)
    {
        clock ??= Clock;
        await using var ctx = TestDatabase.CreateContext(clock, organizationId);
        return await new InvoiceKit(ctx, organizationId, clock).Issue().Handle(
            invoiceId, null, CancellationToken.None);
    }

    private static async Task<InvoiceDto> GetAsync(Guid organizationId, Guid invoiceId)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, organizationId);
        return await new InvoiceKit(ctx, organizationId, Clock).Get().Handle(invoiceId, CancellationToken.None);
    }

    [Fact]
    public async Task CreatingADraft_SavesLinesAndTotals_AndReadsBackExactly()
    {
        var (organizationId, customerId) = await SeedAsync();

        var created = await CreateDraftAsync(organizationId, customerId);
        var read = await GetAsync(organizationId, created.Id);

        Assert.Equal(InvoiceStatus.Draft, read.Status);
        Assert.Null(read.Number);
        Assert.Equal("NGN", read.CurrencyCode);
        Assert.Equal(30, read.PaymentTermsDays);
        Assert.Equal(259.97m, read.Subtotal);
        Assert.Equal(6.00m, read.DiscountTotal);
        Assert.Equal(19.05m, read.TaxTotal);
        Assert.Equal(273.02m, read.Total);
        Assert.Equal(273.02m, read.Balance);
        Assert.Equal(2, read.Lines.Count);
        Assert.Equal(58.02m, read.Lines[0].LineTotal);
        Assert.Equal(215.00m, read.Lines[1].LineTotal);
    }

    [Fact]
    public async Task UpdatingADraft_ReplacesTheLines()
    {
        var (organizationId, customerId) = await SeedAsync();
        var created = await CreateDraftAsync(organizationId, customerId);

        await using (var ctx = TestDatabase.CreateContext(Clock, organizationId))
        {
            await new InvoiceKit(ctx, organizationId, Clock).Update().Handle(
                created.Id,
                new InvoiceInput(customerId, 14, "Updated", [new InvoiceLineInput("Only item", 1m, 50m)]),
                CancellationToken.None);
        }

        var read = await GetAsync(organizationId, created.Id);
        var line = Assert.Single(read.Lines);
        Assert.Equal("Only item", line.Description);
        Assert.Equal(50.00m, read.Total);
        Assert.Equal(14, read.PaymentTermsDays);
        Assert.Equal("Updated", read.Notes);
    }

    [Fact]
    public async Task IssuingInvoices_NumbersThemSequentially_AndSetsTheDueDate()
    {
        var (organizationId, customerId) = await SeedAsync();
        var first = await CreateDraftAsync(organizationId, customerId);
        var second = await CreateDraftAsync(organizationId, customerId);

        var issuedFirst = await IssueAsync(organizationId, first.Id);
        var issuedSecond = await IssueAsync(organizationId, second.Id);

        Assert.Equal("INV-0001", issuedFirst.Number);
        Assert.Equal("INV-0002", issuedSecond.Number);
        Assert.Equal(InvoiceStatus.Issued, issuedFirst.Status);
        Assert.Equal(new DateOnly(2026, 1, 15), issuedFirst.IssueDate);
        Assert.Equal(new DateOnly(2026, 2, 14), issuedFirst.DueDate);
    }

    [Fact]
    public async Task IssuingInParallel_NeverDuplicatesOrSkipsNumbers()
    {
        var (organizationId, customerId) = await SeedAsync();
        var drafts = new List<InvoiceDto>();
        for (var i = 0; i < 5; i++)
            drafts.Add(await CreateDraftAsync(organizationId, customerId));

        var issued = await Task.WhenAll(drafts.Select(d => IssueAsync(organizationId, d.Id)));

        var numbers = issued.Select(i => i.Number).OrderBy(n => n).ToArray();
        Assert.Equal(["INV-0001", "INV-0002", "INV-0003", "INV-0004", "INV-0005"], numbers);
    }

    [Fact]
    public async Task AnIssuedInvoice_CannotBeUpdated()
    {
        var (organizationId, customerId) = await SeedAsync();
        var draft = await CreateDraftAsync(organizationId, customerId);
        await IssueAsync(organizationId, draft.Id);

        await using var ctx = TestDatabase.CreateContext(Clock, organizationId);
        await Assert.ThrowsAsync<DomainException>(() => new InvoiceKit(ctx, organizationId, Clock).Update().Handle(
            draft.Id, new InvoiceInput(customerId, null, null, SampleLines()), CancellationToken.None));
    }

    [Fact]
    public async Task IssuingAnInvoiceWithoutLines_Fails_AndDoesNotConsumeANumber()
    {
        var (organizationId, customerId) = await SeedAsync();
        var empty = await CreateDraftAsync(organizationId, customerId, lines: []);

        await Assert.ThrowsAsync<DomainException>(() => IssueAsync(organizationId, empty.Id));

        var real = await CreateDraftAsync(organizationId, customerId);
        var issued = await IssueAsync(organizationId, real.Id);
        Assert.Equal("INV-0001", issued.Number);
    }

    [Fact]
    public async Task AnInvoice_IsInvisibleToOtherOrganizations()
    {
        var (orgA, customerA) = await SeedAsync();
        var (orgB, _) = await SeedAsync();
        var invoice = await CreateDraftAsync(orgA, customerA);

        await using var ctxB = TestDatabase.CreateContext(Clock, orgB);
        var kitB = new InvoiceKit(ctxB, orgB, Clock);

        await Assert.ThrowsAsync<NotFoundException>(() => kitB.Get().Handle(invoice.Id, CancellationToken.None));

        var listed = await kitB.List().Handle(new ListInvoicesQuery(null, null, null, null, 1, 100), CancellationToken.None);
        Assert.DoesNotContain(listed.Items, i => i.Id == invoice.Id);
    }

    [Fact]
    public async Task CreatingAnInvoice_ForAnotherOrganizationsCustomer_IsRejected()
    {
        var (_, customerA) = await SeedAsync();
        var (orgB, _) = await SeedAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => CreateDraftAsync(orgB, customerA));
    }

    [Fact]
    public async Task CreatingAnInvoice_ForAnArchivedCustomer_IsRejected()
    {
        var (organizationId, customerId) = await SeedAsync(archivedCustomer: true);

        await Assert.ThrowsAsync<DomainException>(() => CreateDraftAsync(organizationId, customerId));
    }

    [Fact]
    public async Task VoidingAnIssuedInvoice_Works_AndKeepsTheRecord()
    {
        var (organizationId, customerId) = await SeedAsync();
        var draft = await CreateDraftAsync(organizationId, customerId);
        await IssueAsync(organizationId, draft.Id);

        await using (var ctx = TestDatabase.CreateContext(Clock, organizationId))
        {
            await new InvoiceKit(ctx, organizationId, Clock).Void().Handle(
                draft.Id, new VoidInvoiceCommand("Raised in error"), CancellationToken.None);
        }

        var read = await GetAsync(organizationId, draft.Id);
        Assert.Equal(InvoiceStatus.Void, read.Status);
        Assert.Equal("Raised in error", read.VoidReason);
        Assert.Equal("INV-0001", read.Number);
    }

    [Fact]
    public async Task TheOverdueFilter_UsesTheOrganizationsToday()
    {
        var (organizationId, customerId) = await SeedAsync();
        var draft = await CreateDraftAsync(organizationId, customerId);
        var issued = await IssueAsync(organizationId, draft.Id); // due 14 Feb 2026

        var later = new FixedTimeProvider(Clock.GetUtcNow().AddDays(60));
        var query = new ListInvoicesQuery(null, null, null, true, 1, 50);

        await using (var ctxNow = TestDatabase.CreateContext(Clock, organizationId))
        {
            var notYet = await new InvoiceKit(ctxNow, organizationId, Clock).List().Handle(query, CancellationToken.None);
            Assert.DoesNotContain(notYet.Items, i => i.Id == issued.Id);
        }

        await using (var ctxLater = TestDatabase.CreateContext(later, organizationId))
        {
            var overdue = await new InvoiceKit(ctxLater, organizationId, later).List().Handle(query, CancellationToken.None);
            Assert.Contains(overdue.Items, i => i.Id == issued.Id && i.IsOverdue);
        }
    }
}

public class InvoicePermissionTests
{
    [Fact]
    public void Staff_CanDraftInvoices_ButNotIssueThem()
    {
        Assert.True(RolePermissions.Has(OrganizationRole.Staff, Permission.ManageInvoices));
        Assert.False(RolePermissions.Has(OrganizationRole.Staff, Permission.IssueInvoices));
    }

    [Fact]
    public void AccountantAndAbove_CanIssueInvoices()
    {
        foreach (var role in new[] { OrganizationRole.Accountant, OrganizationRole.Admin, OrganizationRole.Owner })
            Assert.True(RolePermissions.Has(role, Permission.IssueInvoices));
    }
}