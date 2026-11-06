using System.Globalization;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Customers;
using Application.Organizations;
using Domain.Common;
using Domain.Customers;
using Domain.Invoices;
using Domain.Organizations;

namespace Application.Invoices;

public sealed record SendInvoiceCommand(string? Message);

public sealed record SendInvoiceResult(string SentTo, DateTimeOffset SentAt, int TimesSent);

public sealed class SendInvoiceHandler(
    IInvoiceRepository invoices, ICustomerRepository customers, IOrganizationRepository organizations,
    IInvoicePdfRenderer renderer, IEmailSender emailSender, IUnitOfWork unitOfWork,
    ICurrentOrganization currentOrganization, TimeProvider clock)
{
    private const int MaxMessageLength = 1000;

    public async Task<SendInvoiceResult> Handle(
        Guid id, SendInvoiceCommand command, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var invoice = await invoices.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Invoice not found.");
        var customer = await customers.GetByIdAsync(invoice.CustomerId, cancellationToken)
            ?? throw new NotFoundException("Customer not found.");

        // Every check happens BEFORE anything is sent, so a refusal never leaves a half-done send.
        invoice.EnsureSendable();

        if (string.IsNullOrWhiteSpace(customer.Email))
            throw new DomainException("This customer has no email address. Add one before sending.");

        var message = string.IsNullOrWhiteSpace(command.Message) ? null : command.Message.Trim();
        if (message is { Length: > MaxMessageLength })
            throw new DomainException($"The message can be at most {MaxMessageLength} characters.");

        var model = InvoicePdfModelFactory.Build(
            organization, customer, invoice, InvoiceLookups.Today(organization, clock));
        var pdf = renderer.Render(model);

        var email = new OutgoingEmail(
            customer.Email,
            OneLine($"Invoice {invoice.Number} from {organization.Name}"),
            Body(organization, customer, invoice, message),
            [new EmailAttachment(InvoicePdfModelFactory.FileName(invoice), "application/pdf", pdf)],
            organization.Email);

        if (!await emailSender.SendAsync(email, cancellationToken))
            throw new EmailDeliveryException("The invoice email could not be sent. Please try again later.");

        // Recorded only after the email was accepted for delivery.
        var now = clock.GetUtcNow();
        invoice.MarkSent(customer.Email, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new SendInvoiceResult(customer.Email, now, invoice.SentCount);
    }

    private static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ');

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    private static string Body(Organization organization, Customer customer, Invoice invoice, string? message)
    {
        var currency = invoice.CurrencyCode;
        var lines = new List<string> { $"Hello {customer.Name}.", "" };

        if (message is not null)
        {
            lines.Add(message);
            lines.Add("");
        }

        lines.Add($"Please find invoice {invoice.Number} attached.");
        lines.Add($"Total: {currency} {Money(invoice.Total)}");
        if (invoice.AmountPaid > 0) lines.Add($"Paid so far: {currency} {Money(invoice.AmountPaid)}");
        lines.Add($"Balance due: {currency} {Money(invoice.Balance)}");
        if (invoice.DueDate is { } due)
            lines.Add($"Due date: {due.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}");

        lines.Add("");
        lines.Add("Thank you,");
        lines.Add(organization.Name);

        return string.Join("\n", lines);
    }
}