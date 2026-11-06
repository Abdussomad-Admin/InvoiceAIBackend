using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Invoices;
using Domain.Common;
using Domain.Invoices;
using Infrastructure.Email;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace Tests;

public class InvoiceSendingEntityTests
{
    private static Invoice Issued()
    {
        var invoice = Invoice.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "NGN", 30, null);
        invoice.ReplaceLines([new LineInput("Item", 1m, 100m)]);
        invoice.Issue("INV-0001", new DateOnly(2026, 1, 15));
        return invoice;
    }

    [Fact]
    public void ADraft_CannotBeSent()
    {
        var draft = Invoice.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "NGN", 30, null);

        Assert.Throws<DomainException>(() => draft.EnsureSendable());
        Assert.Throws<DomainException>(() => draft.MarkSent("a@example.com", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void AVoidedInvoice_CannotBeSent()
    {
        var invoice = Issued();
        invoice.Void("Raised in error", DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() => invoice.EnsureSendable());
    }

    [Fact]
    public void MarkSent_RecordsTheRecipientTimeAndCount()
    {
        var invoice = Issued();
        var first = new DateTimeOffset(2026, 1, 16, 9, 0, 0, TimeSpan.Zero);
        var second = first.AddDays(1);

        invoice.MarkSent("billing@zenith.com", first);
        invoice.MarkSent("billing@zenith.com", second);

        Assert.Equal(2, invoice.SentCount);
        Assert.Equal(second, invoice.LastSentAt);
        Assert.Equal("billing@zenith.com", invoice.LastSentTo);
    }
}

public class SmtpMessageTests
{
    [Fact]
    public void BuildMessage_AddsTheAttachmentReplyToAndText()
    {
        var settings = new EmailSettings
        {
            Provider = "Smtp",
            Host = "localhost",
            FromAddress = "no-reply@example.com",
            FromName = "Invoice AI"
        };
        var email = new OutgoingEmail(
            "ada@example.com", "Invoice INV-0001", "Hello there",
            [new EmailAttachment("INV-0001.pdf", "application/pdf", new byte[] { 1, 2, 3 })],
            "billing@acme.com");

        var message = SmtpEmailSender.BuildMessage(settings, email);

        Assert.Equal("ada@example.com", message.To.Mailboxes.Single().Address);
        Assert.Equal("billing@acme.com", message.ReplyTo.Mailboxes.Single().Address);
        Assert.Contains("Hello there", message.TextBody);
        var attachment = Assert.IsType<MimePart>(Assert.Single(message.Attachments));
        Assert.Equal("INV-0001.pdf", attachment.FileName);
    }
}

public class SendInvoiceTests
{
    private static readonly FixedTimeProvider Clock = PaymentTestData.Clock;

    private sealed class FailingEmailSender : IEmailSender
    {
        public Task<bool> SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private static async Task SetCustomerEmailAsync(Seed seed, string? email)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var customer = await ctx.Customers.SingleAsync(c => c.Id == seed.CustomerId);
        customer.Update(customer.Name, email, null, null, null, null, null, null, null, null, null);
        await ctx.SaveChangesAsync();
    }

    private static async Task SetOrganizationEmailAsync(Seed seed, string email)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var organization = await ctx.Organizations.SingleAsync(o => o.Id == seed.OrganizationId);
        organization.UpdateProfile(organization.Name, email, null, null, null, null, null, null, null);
        await ctx.SaveChangesAsync();
    }

    private static async Task<SendInvoiceResult> SendAsync(
        Seed seed, Guid invoiceId, IEmailSender sender, string? message = null)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var handler = new SendInvoiceHandler(
            new InvoiceRepository(ctx), new CustomerRepository(ctx), new OrganizationRepository(ctx),
            new CapturingPdfRenderer(), sender, ctx, new TestCurrentOrganization(seed.OrganizationId), Clock);
        return await handler.Handle(invoiceId, new SendInvoiceCommand(message), CancellationToken.None);
    }

    [Fact]
    public async Task SendingAnInvoice_EmailsTheCustomer_WithThePdfAttached()
    {
        var seed = await PaymentTestData.SeedAsync();
        await SetCustomerEmailAsync(seed, "Billing@Zenith.com");
        await SetOrganizationEmailAsync(seed, "accounts@acme.com");
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed); // 273.02
        var sender = new CapturingEmailSender();

        var result = await SendAsync(seed, invoice.Id, sender, "Thanks for your order");

        var email = Assert.Single(sender.Sent);
        Assert.Equal("billing@zenith.com", email.To);
        Assert.Equal("accounts@acme.com", email.ReplyTo);
        Assert.Contains("INV-0001", email.Subject);
        Assert.Contains("Thanks for your order", email.Body);
        Assert.Contains("NGN 273.02", email.Body);

        var attachment = Assert.Single(email.Attachments!);
        Assert.Equal("INV-0001.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(new byte[] { 1, 2, 3 }, attachment.Content);

        Assert.Equal("billing@zenith.com", result.SentTo);
        Assert.Equal(1, result.TimesSent);

        var read = await PaymentTestData.GetInvoiceAsync(seed, invoice.Id);
        Assert.Equal(1, read.SentCount);
        Assert.NotNull(read.LastSentAt);
    }

    [Fact]
    public async Task SendingTwice_CountsBothSends()
    {
        var seed = await PaymentTestData.SeedAsync();
        await SetCustomerEmailAsync(seed, "billing@zenith.com");
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed);
        var sender = new CapturingEmailSender();

        await SendAsync(seed, invoice.Id, sender);
        var second = await SendAsync(seed, invoice.Id, sender);

        Assert.Equal(2, second.TimesSent);
        Assert.Equal(2, sender.Sent.Count);
    }

    [Fact]
    public async Task WhenTheEmailCannotBeDelivered_NothingIsRecorded()
    {
        var seed = await PaymentTestData.SeedAsync();
        await SetCustomerEmailAsync(seed, "billing@zenith.com");
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed);

        await Assert.ThrowsAsync<EmailDeliveryException>(
            () => SendAsync(seed, invoice.Id, new FailingEmailSender()));

        var read = await PaymentTestData.GetInvoiceAsync(seed, invoice.Id);
        Assert.Equal(0, read.SentCount);
        Assert.Null(read.LastSentAt);
    }

    [Fact]
    public async Task ACustomerWithoutAnEmail_IsRejected_AndNothingIsSent()
    {
        var seed = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed);
        var sender = new CapturingEmailSender();

        await Assert.ThrowsAsync<DomainException>(() => SendAsync(seed, invoice.Id, sender));

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task DraftAndVoidedInvoices_CannotBeSent()
    {
        var seed = await PaymentTestData.SeedAsync();
        await SetCustomerEmailAsync(seed, "billing@zenith.com");
        var sender = new CapturingEmailSender();

        var draft = await PaymentTestData.CreateDraftAsync(seed);
        await Assert.ThrowsAsync<DomainException>(() => SendAsync(seed, draft.Id, sender));

        var voided = await PaymentTestData.IssueInvoiceAsync(seed);
        await using (var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId))
        {
            await new InvoiceKit(ctx, seed.OrganizationId, Clock).Void().Handle(
                voided.Id, new VoidInvoiceCommand("Raised in error"), CancellationToken.None);
        }
        await Assert.ThrowsAsync<DomainException>(() => SendAsync(seed, voided.Id, sender));

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task AnotherOrganization_CannotSendTheInvoice()
    {
        var seedA = await PaymentTestData.SeedAsync();
        var seedB = await PaymentTestData.SeedAsync();
        await SetCustomerEmailAsync(seedA, "billing@zenith.com");
        var invoice = await PaymentTestData.IssueInvoiceAsync(seedA);
        var sender = new CapturingEmailSender();

        await Assert.ThrowsAsync<NotFoundException>(() => SendAsync(seedB, invoice.Id, sender));

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task SendingAnInvoice_IsAudited()
    {
        var seed = await PaymentTestData.SeedAsync();
        await SetCustomerEmailAsync(seed, "billing@zenith.com");
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed);

        await SendAsync(seed, invoice.Id, new CapturingEmailSender());

        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var trail = await ctx.AuditEntries.Where(a => a.EntityId == invoice.Id).ToListAsync();
        Assert.Contains(trail, a => a.Action == "Invoice.Sent" && a.Details!.StartsWith("Sent to billing@zenith.com"));
    }
}