using System.Text;
using Application.Common.Exceptions;
using Application.Customers;
using Application.Invoices;
using Application.Organizations;
using Application.Payments;
using Domain.Customers;
using Domain.Organizations;
using Infrastructure.Pdf;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Tests;

internal sealed class CapturingCustomerStatementRenderer : ICustomerStatementPdfRenderer
{
    public CustomerStatementPdfModel? Model { get; private set; }

    public byte[] Render(CustomerStatementPdfModel model)
    {
        Model = model;
        return [37, 80, 68, 70, 45, 49]; // %PDF-1
    }
}

public class CustomerStatementTests
{
    private static readonly FixedTimeProvider Clock = PaymentTestData.Clock;
    private static readonly DateOnly Today = new(2026, 1, 15);

    private sealed class InMemoryCustomers(Customer customer) : ICustomerRepository
    {
        public Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<Customer?>(id == customer.Id ? customer : null);
        public Task<(IReadOnlyList<Customer> Items, int Total)> SearchAsync(
            string? search, bool includeArchived, int skip, int take, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public void Add(Customer value) => throw new NotSupportedException();
    }

    private sealed class InMemoryOrganizations(Organization organization) : IOrganizationRepository
    {
        public Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<Organization?>(id == organization.Id ? organization : null);
        public void Add(Organization value) => throw new NotSupportedException();
    }

    private sealed class InMemoryStatements(CustomerStatementData data) : ICustomerStatementRepository
    {
        public Task<CustomerStatementData> GetAsync(
            Guid customerId, DateOnly from, DateOnly to, DateTimeOffset startUtc,
            DateTimeOffset endExclusiveUtc, CancellationToken cancellationToken = default)
            => Task.FromResult(data);
    }

    private static GetCustomerStatementHandler Handler(AppDbContext ctx, Seed seed, TimeProvider? clock = null)
        => new(new CustomerRepository(ctx), new CustomerStatementRepository(ctx),
            new OrganizationRepository(ctx), new TestCurrentOrganization(seed.OrganizationId), clock ?? Clock);

    private static async Task<InvoiceDto> IssueOnAsync(Seed seed, DateOnly issueDate)
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(
            issueDate.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero));
        InvoiceDto draft;
        await using (var ctx = TestDatabase.CreateContext(clock, seed.OrganizationId, seed.UserId))
        {
            draft = await new InvoiceKit(ctx, seed.OrganizationId, clock).Create().Handle(
                new InvoiceInput(seed.CustomerId, null, null, PaymentTestData.Lines), CancellationToken.None);
        }

        await using var issueContext = TestDatabase.CreateContext(clock, seed.OrganizationId, seed.UserId);
        return await new InvoiceKit(issueContext, seed.OrganizationId, clock).Issue()
            .Handle(draft.Id, null, CancellationToken.None);
    }

    private static async Task SetCustomerEmailAsync(Seed seed, string email)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var customer = await ctx.Customers.SingleAsync(c => c.Id == seed.CustomerId);
        customer.Update(customer.Name, email, null, null, null, null, null, null, null, null, null);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Statement_ComputesOpeningAndClosingBalancesAcrossCorrectingEntries()
    {
        var organization = Organization.Create("Acme", "NG", "NGN", "Africa/Lagos");
        var customer = Customer.Create(organization.Id, "Zenith Traders");
        var voidedAt = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var reversedAt = new DateTimeOffset(2026, 1, 15, 10, 15, 0, TimeSpan.Zero);
        var data = new CustomerStatementData(
            new CustomerStatementOpeningParts(300m, 50m, 100m, 25m),
            [
                new CustomerStatementInvoiceRow(Guid.NewGuid(), "INV-0002", Today, 200m, "NGN", null),
                new CustomerStatementInvoiceRow(Guid.NewGuid(), "INV-0001", Today.AddDays(-1), 50m, "NGN", voidedAt)
            ],
            [
                new CustomerStatementPaymentRow(Guid.NewGuid(), Today, 30m, "NGN", Domain.Payments.PaymentMethod.BankTransfer, "PAY-2", null),
                new CustomerStatementPaymentRow(Guid.NewGuid(), Today.AddDays(-1), 25m, "NGN", Domain.Payments.PaymentMethod.Card, "PAY-1", reversedAt)
            ]);
        var handler = new GetCustomerStatementHandler(
            new InMemoryCustomers(customer), new InMemoryStatements(data),
            new InMemoryOrganizations(organization), new TestCurrentOrganization(organization.Id), Clock);

        var statement = await handler.Handle(customer.Id, Today, Today, CancellationToken.None);

        Assert.Equal(175m, statement.OpeningBalance);
        Assert.Equal(320m, statement.ClosingBalance);
        Assert.Equal(4, statement.Entries.Count);
        Assert.Equal(320m, statement.Entries[^1].Balance);
        Assert.Contains(statement.Entries, entry => entry.Type == CustomerStatementEntryType.InvoiceVoid && entry.Credit == 50m);
        Assert.Contains(statement.Entries, entry => entry.Type == CustomerStatementEntryType.PaymentReversal && entry.Debit == 25m);
    }

    [Fact]
    public async Task Statement_CarriesForwardOpeningBalance_AndBuildsRunningBalance()
    {
        var seed = await PaymentTestData.SeedAsync();
        var priorInvoice = await IssueOnAsync(seed, new DateOnly(2026, 1, 10));
        await PaymentTestData.RecordAsync(seed, priorInvoice.Id, 50m, paidOn: new DateOnly(2026, 1, 12));
        var reversal = await PaymentTestData.HistoryAsync(seed, priorInvoice.Id);
        await PaymentTestData.ReverseAsync(seed, reversal.Single().Id, "Incorrect payment");

        var invoice = await PaymentTestData.IssueInvoiceAsync(seed);
        await PaymentTestData.RecordAsync(seed, invoice.Id, 25m, paidOn: Today);

        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var statement = await Handler(ctx, seed).Handle(seed.CustomerId, Today, Today, CancellationToken.None);

        Assert.Equal(223.02m, statement.OpeningBalance);
        Assert.Equal(3, statement.Entries.Count);
        Assert.Equal(CustomerStatementEntryType.Invoice, statement.Entries[0].Type);
        Assert.Equal(CustomerStatementEntryType.Payment, statement.Entries[1].Type);
        Assert.Equal(CustomerStatementEntryType.PaymentReversal, statement.Entries[2].Type);
        Assert.Equal(521.04m, statement.ClosingBalance);
        Assert.Equal(statement.ClosingBalance, statement.Entries[^1].Balance);
    }

    [Fact]
    public async Task Statement_VoidInThePeriodReversesAnEarlierInvoiceCharge()
    {
        var seed = await PaymentTestData.SeedAsync();
        var invoice = await IssueOnAsync(seed, new DateOnly(2026, 1, 10));

        await using (var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId))
            await new InvoiceKit(ctx, seed.OrganizationId, Clock).Void().Handle(
                invoice.Id, new VoidInvoiceCommand("Raised in error"), CancellationToken.None);

        await using var read = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var statement = await Handler(read, seed).Handle(seed.CustomerId, Today, Today, CancellationToken.None);

        Assert.Equal(invoice.Total, statement.OpeningBalance);
        Assert.Equal(0m, statement.ClosingBalance);
        Assert.Equal(CustomerStatementEntryType.InvoiceVoid, Assert.Single(statement.Entries).Type);
        Assert.Equal(invoice.Total, statement.Entries[0].Credit);
    }

    [Fact]
    public async Task Statement_CannotReadAnotherOrganizationsCustomer()
    {
        var owned = await PaymentTestData.SeedAsync();
        var other = await PaymentTestData.SeedAsync();

        await using var ctx = TestDatabase.CreateContext(Clock, other.OrganizationId, other.UserId);

        await Assert.ThrowsAsync<NotFoundException>(() => Handler(ctx, other).Handle(
            owned.CustomerId, Today, Today, CancellationToken.None));
    }

    [Fact]
    public async Task SendStatement_AttachesPdfAndWritesAuditRecord()
    {
        var seed = await PaymentTestData.SeedAsync();
        await SetCustomerEmailAsync(seed, "billing@example.com");
        await PaymentTestData.IssueInvoiceAsync(seed);
        var renderer = new CapturingCustomerStatementRenderer();
        var email = new CapturingEmailSender();

        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var currentOrganization = new TestCurrentOrganization(seed.OrganizationId);
        var get = Handler(ctx, seed);
        var pdf = new GetCustomerStatementPdfHandler(get, new CustomerRepository(ctx),
            new OrganizationRepository(ctx), currentOrganization, renderer);
        var send = new SendCustomerStatementHandler(pdf, email, new AuditRepository(ctx), ctx,
            currentOrganization, new TestCurrentUser(seed.UserId));

        var result = await send.Handle(seed.CustomerId,
            new SendCustomerStatementCommand(Today, Today), CancellationToken.None);

        var sent = Assert.Single(email.Sent);
        Assert.Equal("billing@example.com", sent.To);
        Assert.Contains("Statement of account", sent.Subject);
        Assert.Equal("application/pdf", Assert.Single(sent.Attachments!).ContentType);
        Assert.Equal(new byte[] { 37, 80, 68, 70, 45, 49 }, sent.Attachments![0].Content);
        Assert.Equal(273.02m, renderer.Model!.ClosingBalance);
        Assert.Contains("customer-statement-", result.FileName);

        var audit = await ctx.AuditEntries.Where(a => a.EntityId == seed.CustomerId).ToListAsync();
        Assert.Contains(audit, a => a.Action == "CustomerStatement.Sent");
    }

    [Fact]
    public void PdfRenderer_ProducesAValidPdf()
    {
        var party = new Application.Invoices.PdfParty("Acme", ["Lagos, NG"], null, null, null);
        var model = new CustomerStatementPdfModel(party, party, "NGN", Today, Today,
            10m, [new CustomerStatementEntry(Today, CustomerStatementEntryType.Invoice,
                "INV-0001", "Invoice issued", 25m, 0m, 35m)], 35m);

        var bytes = new QuestPdfCustomerStatementRenderer().Render(model);

        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    }
}
