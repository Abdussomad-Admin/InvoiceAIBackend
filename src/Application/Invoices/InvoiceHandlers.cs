using Application.Common;
using Application.Billing;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Customers;
using Application.Organizations;
using Domain.Common;
using Domain.Customers;
using Domain.Invoices;
using Domain.Organizations;

namespace Application.Invoices;

internal static class InvoiceLookups
{
    public static async Task<Organization> OrganizationAsync(
        IOrganizationRepository organizations, ICurrentOrganization current, CancellationToken cancellationToken)
        => await organizations.GetByIdAsync(current.RequireId(), cancellationToken)
           ?? throw new NotFoundException("Organization not found.");

    public static async Task<Customer> ActiveCustomerAsync(
        ICustomerRepository customers, Guid id, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Customer not found.");
        if (customer.IsArchived) throw new DomainException("This customer is archived.");
        return customer;
    }

    public static async Task<string> CustomerNameAsync(
        ICustomerRepository customers, Guid id, CancellationToken cancellationToken)
        => (await customers.GetByIdAsync(id, cancellationToken))?.Name ?? "(unknown customer)";

    public static IEnumerable<LineInput> ToLineInputs(InvoiceInput input)
        => (input.Lines ?? Array.Empty<InvoiceLineInput>()).Select(l =>
            new LineInput(l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxRatePercent));

    // "Today" in the organization's own time zone, so due dates and overdue follow local dates.
    public static DateOnly Today(Organization organization, TimeProvider clock)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(organization.TimeZoneId);
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);
        return DateOnly.FromDateTime(local.DateTime);
    }
}

internal static class InvoiceMapping
{
    public static InvoiceDto ToDto(this Invoice i, string customerName, DateOnly today) => new(
    i.Id, i.Number, i.CustomerId, customerName, i.CurrencyCode, i.Status, i.IssueDate, i.DueDate,
    i.PaymentTermsDays, i.Notes, i.Subtotal, i.DiscountTotal, i.TaxTotal, i.Total,
    i.AmountPaid, i.AmountCredited, i.Balance, i.BaseCurrencyCode, i.ExchangeRateToBase,
    i.IsOverdue(today), i.DaysOverdue(today), i.VoidReason,
    i.Lines.OrderBy(l => l.Position).Select(l => new InvoiceLineDto(
        l.Position, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxRatePercent,
        l.LineSubtotal, l.DiscountAmount, l.TaxAmount, l.LineTotal)).ToList(),
    i.LastSentAt, i.SentCount);
}

public sealed class CreateInvoiceHandler(
    IInvoiceRepository invoices, ICustomerRepository customers, IOrganizationRepository organizations,
    IUnitOfWork unitOfWork, ICurrentOrganization currentOrganization, TimeProvider clock,
    IPlanLimitChecker? planLimits = null)
{
    public async Task<InvoiceDto> Handle(InvoiceInput input, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        if (planLimits is not null) await planLimits.EnsureInvoiceCapacityAsync(cancellationToken);
        var customer = await InvoiceLookups.ActiveCustomerAsync(customers, input.CustomerId, cancellationToken);
        var currencyCode = (input.CurrencyCode ?? organization.BaseCurrencyCode).Trim().ToUpperInvariant();
        if (currencyCode != organization.BaseCurrencyCode && input.ExchangeRateToBase is null)
            throw new DomainException($"Enter the {currencyCode} to {organization.BaseCurrencyCode} exchange rate for this invoice.");

        var invoice = Invoice.CreateDraft(
            organization.Id, customer.Id, currencyCode,
            input.PaymentTermsDays ?? organization.DefaultPaymentTermsDays, input.Notes,
            organization.BaseCurrencyCode, input.ExchangeRateToBase ?? 1m);
        invoice.ReplaceLines(InvoiceLookups.ToLineInputs(input));

        invoices.Add(invoice);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return invoice.ToDto(customer.Name, InvoiceLookups.Today(organization, clock));
    }
}

public sealed class UpdateInvoiceHandler(
    IInvoiceRepository invoices, ICustomerRepository customers, IOrganizationRepository organizations,
    IUnitOfWork unitOfWork, ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<InvoiceDto> Handle(Guid id, InvoiceInput input, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var invoice = await invoices.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Invoice not found.");
        var customer = await InvoiceLookups.ActiveCustomerAsync(customers, input.CustomerId, cancellationToken);

        invoice.UpdateDetails(customer.Id, input.PaymentTermsDays ?? invoice.PaymentTermsDays, input.Notes,
            input.CurrencyCode, input.ExchangeRateToBase);
        invoice.ReplaceLines(InvoiceLookups.ToLineInputs(input));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return invoice.ToDto(customer.Name, InvoiceLookups.Today(organization, clock));
    }
}

public sealed class IssueInvoiceHandler(
    IInvoiceRepository invoices, ICustomerRepository customers, IOrganizationRepository organizations,
    IInvoiceNumberGenerator numbers, ITransactionRunner transactions, IUnitOfWork unitOfWork,
    ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<InvoiceDto> Handle(Guid id, DateOnly? dueDate, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var today = InvoiceLookups.Today(organization, clock);
        Invoice? issued = null;

        // The number and the status change are saved together. If anything fails, the
        // transaction rolls back, including the number, so numbers are never skipped.
        await transactions.RunInTransactionAsync(async () =>
        {
            var invoice = await invoices.GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException("Invoice not found.");

            var number = await numbers.NextAsync(organization.Id, cancellationToken);
            invoice.Issue(number, today, dueDate);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            issued = invoice;
        }, cancellationToken);

        var name = await InvoiceLookups.CustomerNameAsync(customers, issued!.CustomerId, cancellationToken);
        return issued.ToDto(name, today);
    }
}

public sealed class VoidInvoiceHandler(
    IInvoiceRepository invoices, ICustomerRepository customers, IOrganizationRepository organizations,
    IUnitOfWork unitOfWork, ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<InvoiceDto> Handle(Guid id, VoidInvoiceCommand command, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var invoice = await invoices.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Invoice not found.");

        invoice.Void(command.Reason, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var name = await InvoiceLookups.CustomerNameAsync(customers, invoice.CustomerId, cancellationToken);
        return invoice.ToDto(name, InvoiceLookups.Today(organization, clock));
    }
}

public sealed class GetInvoiceHandler(
    IInvoiceRepository invoices, ICustomerRepository customers, IOrganizationRepository organizations,
    ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<InvoiceDto> Handle(Guid id, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var invoice = await invoices.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Invoice not found.");

        var name = await InvoiceLookups.CustomerNameAsync(customers, invoice.CustomerId, cancellationToken);
        return invoice.ToDto(name, InvoiceLookups.Today(organization, clock));
    }
}

public sealed class ListInvoicesHandler(
    IInvoiceRepository invoices, IOrganizationRepository organizations,
    ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<PagedResult<InvoiceSummaryDto>> Handle(ListInvoicesQuery query, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, total) = await invoices.SearchAsync(
            new InvoiceSearch(query.Search, query.Status, query.CustomerId, query.Overdue),
            InvoiceLookups.Today(organization, clock), (page - 1) * pageSize, pageSize, cancellationToken);

        return new PagedResult<InvoiceSummaryDto>(items, total, page, pageSize);
    }
}

public sealed class ExportInvoicesHandler(IInvoiceRepository invoices)
{
    public Task<IReadOnlyList<InvoiceExportRow>> Handle(CancellationToken cancellationToken)
        => invoices.ExportAsync(cancellationToken);
}

public sealed class CreditNoteHandlers(IInvoiceRepository invoices, IOrganizationRepository organizations,
    ICurrentOrganization current, IUnitOfWork unitOfWork, ITransactionRunner transactions, TimeProvider clock)
{
    public async Task<IReadOnlyList<CreditNoteDto>> List(Guid invoiceId, CancellationToken ct)
    {
        _ = await InvoiceLookups.OrganizationAsync(organizations, current, ct);
        _ = await invoices.GetByIdAsync(invoiceId, ct) ?? throw new NotFoundException("Invoice not found.");
        return await invoices.CreditNotesAsync(invoiceId, ct);
    }

    public async Task<CreditNoteDto> Create(Guid invoiceId, CreateCreditNoteCommand command, CancellationToken ct)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, current, ct);
        CreditNote? created = null;
        await transactions.RunInTransactionAsync(async () =>
        {
            if (!await invoices.LockAsync(invoiceId, ct)) throw new NotFoundException("Invoice not found.");
            var invoice = await invoices.GetByIdAsync(invoiceId, ct) ?? throw new NotFoundException("Invoice not found.");
            if (invoice.Status is InvoiceStatus.Draft or InvoiceStatus.Void)
                throw new DomainException("A credit note can only be issued for an active invoice.");
            invoice.ApplyCredit(command.Amount);
            var number = $"CN-{Guid.NewGuid():N}"[..15].ToUpperInvariant();
            created = CreditNote.Create(organization.Id, invoice.Id, number, invoice.CurrencyCode,
                invoice.BaseCurrencyCode, invoice.ExchangeRateToBase,
                command.Amount, command.Reason, InvoiceLookups.Today(organization, clock));
            invoices.AddCreditNote(created);
            await unitOfWork.SaveChangesAsync(ct);
        }, ct);
        return new CreditNoteDto(created!.Id, created.InvoiceId, "", created.Number, created.CurrencyCode,
            created.BaseCurrencyCode, created.ExchangeRateToBase,
            created.Amount, created.Reason, created.IssuedOn, created.CreatedAt);
    }
}
