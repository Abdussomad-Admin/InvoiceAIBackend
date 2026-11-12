using System.Globalization;
using System.Net;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Customers;
using Application.Organizations;
using Domain.Common;
using Domain.Customers;
using Domain.Invoices;
using Domain.Organizations;

using Application.Common;

namespace Application.Invoices;

public sealed record SendInvoiceCommand(string? Message);

public sealed record SendInvoiceResult(string SentTo, DateTimeOffset SentAt, int TimesSent);

public sealed class SendInvoiceHandler(
    IInvoiceRepository invoices, ICustomerRepository customers, IOrganizationRepository organizations,
    IInvoicePdfRenderer renderer, IEmailSender emailSender, IUnitOfWork unitOfWork,
    ICurrentOrganization currentOrganization, AppLinkSettings links, TimeProvider clock)
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
        var paymentToken = invoice.EnsurePublicPaymentToken();
        var paymentUrl = $"{links.FrontendBaseUrl.TrimEnd('/')}/?pay={Uri.EscapeDataString(paymentToken)}";

        var email = new OutgoingEmail(
            customer.Email,
            OneLine($"Invoice {invoice.Number} from {organization.Name}"),
            Body(organization, customer, invoice, message, paymentUrl),
            [new EmailAttachment(InvoicePdfModelFactory.FileName(invoice), "application/pdf", pdf)],
            organization.Email,
            HtmlBody: HtmlBody(organization, customer, invoice, message, paymentUrl));

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

    private static string Body(Organization organization, Customer customer, Invoice invoice, string? message, string paymentUrl)
    {
        var currency = invoice.CurrencyCode;
        var lines = new List<string> { $"Hello {customer.Name},", "" };

        if (message is not null)
        {
            lines.Add(message);
            lines.Add("");
        }

        lines.Add($"Your invoice {invoice.Number} from {organization.Name} is attached.");
        lines.Add("");
        lines.Add($"Invoice total: {currency} {Money(invoice.Total)}");
        if (invoice.AmountPaid > 0) lines.Add($"Paid so far: {currency} {Money(invoice.AmountPaid)}");
        lines.Add($"Balance due: {currency} {Money(invoice.Balance)}");
        if (invoice.DueDate is { } due)
            lines.Add($"Payment due: {due.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}");
        if (invoice.Balance > 0)
        {
            lines.Add("");
            lines.Add($"Pay now: {paymentUrl}");
        }

        lines.Add("");
        lines.Add("Thank you,");
        lines.Add(organization.Name);

        return string.Join("\n", lines);
    }
    private static string HtmlBody(Organization organization, Customer customer, Invoice invoice, string? message, string paymentUrl)
    {
        static string Safe(string? value) => WebUtility.HtmlEncode(value ?? "");
        var dueDate = invoice.DueDate is { } due
            ? due.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
            : "No due date set";
        var note = string.IsNullOrWhiteSpace(message)
            ? ""
            : $"<div style=\"margin:0 0 20px;padding:13px 15px;border-left:3px solid #8a63e9;border-radius:4px;background:#f7f4ff;color:#51466f;font-size:14px;line-height:1.6\">{Safe(message).Replace("\r\n", "<br>").Replace("\n", "<br>")}</div>";
        var paidRow = invoice.AmountPaid > 0
            ? $"<tr><td style=\"padding:9px 0;color:#716b86\">Paid so far</td><td style=\"padding:9px 0;text-align:right;color:#33295a\">{Safe(invoice.CurrencyCode)} {Money(invoice.AmountPaid)}</td></tr>"
            : "";
        var payButton = invoice.Balance > 0
            ? $"<tr><td colspan=\"2\" style=\"padding:16px 0 2px;text-align:center\"><a href=\"{Safe(paymentUrl)}\" style=\"display:inline-block;padding:14px 32px;border-radius:10px;background:#6c40df;color:#ffffff;text-decoration:none;font-family:Arial,Helvetica,sans-serif;font-size:15px;font-weight:700\">Pay now</a></td></tr>"
            : "";

        return $"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:30px 14px;background:#f5f3fc;font-family:Arial,Helvetica,sans-serif;color:#241a4d">
              <div style="max-width:580px;margin:0 auto;overflow:hidden;border:1px solid #e8e2f3;border-radius:16px;background:#ffffff">
                <div style="padding:21px 28px;background:#211747;color:#ffffff"><div style="font-size:19px;font-weight:700">Invoice <span style="color:#b79aff">AI</span></div><div style="margin-top:5px;color:#d5cbed;font-size:12px">A clear summary of your invoice</div></div>
                <div style="padding:28px">
                  <p style="margin:0 0 9px;color:#241a4d;font-size:16px;font-weight:700">Hello {Safe(customer.Name)},</p>
                  {note}
                  <p style="margin:0 0 18px;color:#746e88;font-size:14px;line-height:1.6">Your invoice from <strong style="color:#33295a">{Safe(organization.Name)}</strong> is attached to this email.</p>
                  <div style="padding:17px 19px;border:1px solid #e8e2f3;border-radius:12px;background:#fbfaff">
                    <div style="padding-bottom:12px;border-bottom:1px solid #ece7f5;color:#726b87;font-size:12px">INVOICE <strong style="color:#493287">{Safe(invoice.Number)}</strong></div>
                    <table role="presentation" style="width:100%;border-collapse:collapse;font-size:13px">
                      <tr><td style="padding:12px 0 9px;color:#716b86">Invoice total</td><td style="padding:12px 0 9px;text-align:right;color:#33295a">{Safe(invoice.CurrencyCode)} {Money(invoice.Total)}</td></tr>
                      {paidRow}
                      <tr><td style="padding:10px 0;border-top:1px solid #e8e2f3;color:#493287;font-weight:700">Balance due</td><td style="padding:10px 0;border-top:1px solid #e8e2f3;text-align:right;color:#493287;font-size:17px;font-weight:700">{Safe(invoice.CurrencyCode)} {Money(invoice.Balance)}</td></tr>
                      <tr><td colspan="2" style="padding-top:7px;color:#827b96;font-size:12px">Payment due: <strong style="color:#554b70">{dueDate}</strong></td></tr>
                      {payButton}
                    </table>
                  </div>
                  <p style="margin:22px 0 0;color:#746e88;font-size:13px;line-height:1.6">If you have already paid, you can disregard this reminder. For questions about this invoice, reply to this email.</p>
                  <p style="margin:20px 0 0;color:#51466f;font-size:13px;line-height:1.6">Thank you,<br><strong>{Safe(organization.Name)}</strong></p>
                </div>
                <div style="padding:14px 28px;border-top:1px solid #eeeaf5;background:#fbfaff;color:#918aa4;font-size:11px">Sent securely with Invoice AI</div>
              </div>
            </body>
            </html>
            """;
    }
}
