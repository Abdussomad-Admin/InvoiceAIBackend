using System.Globalization;
using Application.Audit;
using Application.Common;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Invoices;
using Application.Organizations;
using Domain.Audit;
using Domain.Common;
using Domain.Customers;
using Domain.Invoices;
using Domain.Organizations;
using Domain.Payments;

namespace Application.Customers;

public enum CustomerStatementEntryType
{
    Invoice,
    CreditNote,
    Payment,
    InvoiceVoid,
    PaymentReversal
}

public sealed record CustomerStatementEntry(
    DateOnly Date, CustomerStatementEntryType Type, string Reference, string Description,
    decimal Debit, decimal Credit, decimal Balance);

public sealed record CustomerStatementDto(
    Guid CustomerId, string CustomerName, string CurrencyCode, DateOnly From, DateOnly To,
    decimal OpeningBalance, IReadOnlyList<CustomerStatementEntry> Entries, decimal ClosingBalance);

public sealed record CustomerStatementInvoiceRow(
    Guid Id, string? Number, DateOnly? IssueDate, decimal TotalBase, string CurrencyCode, DateTimeOffset? VoidedAt);

public sealed record CustomerStatementPaymentRow(
    Guid Id, DateOnly PaidOn, decimal AmountBase, string CurrencyCode, PaymentMethod Method,
    string? Reference, DateTimeOffset? ReversedAt);

public sealed record CustomerStatementCreditNoteRow(string Number, decimal AmountBase, DateOnly IssuedOn, string Reason);

public sealed record CustomerStatementOpeningParts(
    decimal Invoices, decimal VoidedInvoices, decimal Payments, decimal ReversedPayments, decimal Credits);

public sealed record CustomerStatementData(
    CustomerStatementOpeningParts Opening, IReadOnlyList<CustomerStatementInvoiceRow> Invoices,
    IReadOnlyList<CustomerStatementPaymentRow> Payments, IReadOnlyList<CustomerStatementCreditNoteRow> CreditNotes);

public interface ICustomerStatementRepository
{
    Task<CustomerStatementData> GetAsync(
        Guid customerId, DateOnly from, DateOnly to, DateTimeOffset startUtc,
        DateTimeOffset endExclusiveUtc, CancellationToken cancellationToken = default);
}

public sealed class GetCustomerStatementHandler(
    ICustomerRepository customers, ICustomerStatementRepository statements,
    IOrganizationRepository organizations, ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<CustomerStatementDto> Handle(
        Guid customerId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (from is null || to is null)
            throw new DomainException("Both statement start and end dates are required.");
        if (from > to)
            throw new DomainException("The statement start date cannot be after its end date.");

        var organizationId = currentOrganization.RequireId();
        var organization = await organizations.GetByIdAsync(organizationId, cancellationToken)
            ?? throw new NotFoundException("Organization not found.");
        var customer = await customers.GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException("Customer not found.");

        var zone = TimeZoneInfo.FindSystemTimeZoneById(organization.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
        if (to > today)
            throw new DomainException("The statement end date cannot be in the future.");

        var startUtc = UtcStartOfLocalDate(from.Value, zone);
        var endExclusiveUtc = UtcStartOfLocalDate(to.Value.AddDays(1), zone);
        var data = await statements.GetAsync(
            customerId, from.Value, to.Value, startUtc, endExclusiveUtc, cancellationToken);

        var opening = MoneyMath.Round(
            data.Opening.Invoices - data.Opening.VoidedInvoices
            - data.Opening.Payments + data.Opening.ReversedPayments - data.Opening.Credits);
        var ledger = new List<(DateOnly Date, int Order, CustomerStatementEntryType Type,
            string Reference, string Description, decimal Debit, decimal Credit)>();

        foreach (var invoice in data.Invoices)
        {
            if (invoice.IssueDate is { } issueDate && issueDate >= from && issueDate <= to)
                ledger.Add((issueDate, 10, CustomerStatementEntryType.Invoice,
                    invoice.Number ?? $"Invoice {invoice.Id.ToString("N")[..8]}", "Invoice issued",
                    invoice.TotalBase, 0m));

            if (invoice.VoidedAt is { } voidedAt && voidedAt >= startUtc && voidedAt < endExclusiveUtc)
            {
                var voidDate = LocalDate(voidedAt, zone);
                ledger.Add((voidDate, 30, CustomerStatementEntryType.InvoiceVoid,
                    invoice.Number ?? $"Invoice {invoice.Id.ToString("N")[..8]}", "Invoice voided",
                    0m, invoice.TotalBase));
            }
        }

        foreach (var payment in data.Payments)
        {
            var reference = string.IsNullOrWhiteSpace(payment.Reference)
                ? $"Payment {payment.Id.ToString("N")[..8]}"
                : payment.Reference;

            if (payment.PaidOn >= from && payment.PaidOn <= to)
                ledger.Add((payment.PaidOn, 20, CustomerStatementEntryType.Payment,
                    reference, $"Payment received ({payment.Method})", 0m, payment.AmountBase));

            if (payment.ReversedAt is { } reversedAt && reversedAt >= startUtc && reversedAt < endExclusiveUtc)
            {
                var reversalDate = LocalDate(reversedAt, zone);
                ledger.Add((reversalDate, 40, CustomerStatementEntryType.PaymentReversal,
                    reference, "Payment reversed", payment.AmountBase, 0m));
            }

        foreach (var credit in data.CreditNotes.Where(c => c.IssuedOn >= from && c.IssuedOn <= to))
            ledger.Add((credit.IssuedOn, 25, CustomerStatementEntryType.CreditNote,
                credit.Number, $"Credit note issued: {credit.Reason}", 0m, credit.AmountBase));
        }

        var balance = opening;
        var entries = new List<CustomerStatementEntry>(ledger.Count);
        foreach (var item in ledger.OrderBy(x => x.Date).ThenBy(x => x.Order).ThenBy(x => x.Reference, StringComparer.Ordinal))
        {
            balance = MoneyMath.Round(balance + item.Debit - item.Credit);
            entries.Add(new CustomerStatementEntry(
                item.Date, item.Type, item.Reference, item.Description,
                item.Debit, item.Credit, balance));
        }

        return new CustomerStatementDto(
            customer.Id, customer.Name, organization.BaseCurrencyCode, from.Value, to.Value,
            opening, entries, balance);
    }

    private static DateOnly LocalDate(DateTimeOffset value, TimeZoneInfo zone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, zone).DateTime);

    private static DateTimeOffset UtcStartOfLocalDate(DateOnly date, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }
}

public sealed record CustomerStatementPdfModel(
    PdfParty Seller, PdfParty Buyer, string CurrencyCode, DateOnly From, DateOnly To,
    decimal OpeningBalance, IReadOnlyList<CustomerStatementEntry> Entries, decimal ClosingBalance);

public sealed record CustomerStatementPdfFile(
    byte[] Content, string FileName, string CustomerName, string CustomerEmail,
    string OrganizationName, string? OrganizationEmail, string CurrencyCode,
    DateOnly From, DateOnly To, decimal ClosingBalance);

public interface ICustomerStatementPdfRenderer
{
    byte[] Render(CustomerStatementPdfModel model);
}

public sealed class GetCustomerStatementPdfHandler(
    GetCustomerStatementHandler getStatement, ICustomerRepository customers,
    IOrganizationRepository organizations, ICurrentOrganization currentOrganization,
    ICustomerStatementPdfRenderer renderer)
{
    public async Task<CustomerStatementPdfFile> Handle(
        Guid customerId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var statement = await getStatement.Handle(customerId, from, to, cancellationToken);
        var customer = await customers.GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException("Customer not found.");
        var organization = await organizations.GetByIdAsync(currentOrganization.RequireId(), cancellationToken)
            ?? throw new NotFoundException("Organization not found.");

        var seller = new PdfParty(organization.Name,
            PdfAddress.Lines(organization.AddressLine1, organization.AddressLine2, organization.City,
                organization.Region, organization.PostalCode, organization.CountryCode),
            organization.Email, organization.Phone, organization.TaxIdentificationNumber);
        var buyer = new PdfParty(customer.Name,
            PdfAddress.Lines(customer.AddressLine1, customer.AddressLine2, customer.City,
                customer.Region, customer.PostalCode, customer.CountryCode),
            customer.Email, customer.Phone, customer.TaxIdentificationNumber);
        var model = new CustomerStatementPdfModel(seller, buyer, statement.CurrencyCode,
            statement.From, statement.To, statement.OpeningBalance, statement.Entries, statement.ClosingBalance);
        var fileName = $"customer-statement-{customer.Id.ToString("N")[..8]}-{statement.From:yyyyMMdd}-{statement.To:yyyyMMdd}.pdf";

        return new CustomerStatementPdfFile(renderer.Render(model), fileName, customer.Name,
            customer.Email ?? "", organization.Name, organization.Email, statement.CurrencyCode,
            statement.From, statement.To, statement.ClosingBalance);
    }
}

public sealed record SendCustomerStatementCommand(DateOnly? From, DateOnly? To);
public sealed record SendCustomerStatementResult(string SentTo, string FileName);

public sealed class SendCustomerStatementHandler(
    GetCustomerStatementPdfHandler statementPdf, IEmailSender emailSender,
    IAuditRepository audit, IUnitOfWork unitOfWork,
    ICurrentOrganization currentOrganization, ICurrentUser currentUser)
{
    public async Task<SendCustomerStatementResult> Handle(
        Guid customerId, SendCustomerStatementCommand command, CancellationToken cancellationToken)
    {
        var file = await statementPdf.Handle(customerId, command.From, command.To, cancellationToken);
        if (string.IsNullOrWhiteSpace(file.CustomerEmail))
            throw new DomainException("This customer has no email address. Add one before sending a statement.");

        var period = $"{file.From.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)} to {file.To.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}";
        var subject = $"Statement of account from {OneLine(file.OrganizationName)}";
        var body = string.Join("\n", $"Hello {file.CustomerName}.", "",
            $"Please find your statement of account for {period} attached.",
            $"Closing balance: {file.CurrencyCode} {file.ClosingBalance.ToString("N2", CultureInfo.InvariantCulture)}", "",
            "Thank you,", file.OrganizationName);
        var email = new OutgoingEmail(file.CustomerEmail, subject, body,
            [new EmailAttachment(file.FileName, "application/pdf", file.Content)], file.OrganizationEmail);

        if (!await emailSender.SendAsync(email, cancellationToken))
            throw new EmailDeliveryException("The customer statement email could not be sent. Please try again later.");

        audit.Add(AuditEntry.Create(currentOrganization.RequireId(), currentUser.UserId,
            "CustomerStatement.Sent", "Customer", customerId,
            $"Statement emailed to {file.CustomerEmail} for {period}; closing balance {file.CurrencyCode} {file.ClosingBalance.ToString("N2", CultureInfo.InvariantCulture)}"));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new SendCustomerStatementResult(file.CustomerEmail, file.FileName);
    }

    private static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
