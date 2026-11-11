using System.Globalization;
using Application.Common.Interfaces;
using Domain.Email;
using Domain.Invoices;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Host.Background;

public sealed class EmailOutboxProcessor(
    AppDbContext db, IEmailSender sender, TimeProvider clock, ReminderSettings settings,
    ILogger<EmailOutboxProcessor> logger)
{
    private static readonly (string Key, int OffsetDays, string Label)[] ReminderPoints =
    [
        ("before-7", -7, "is due in 7 days"),
        ("due", 0, "is due today"),
        ("overdue-7", 7, "is 7 days overdue"),
        ("overdue-30", 30, "is 30 days overdue")
    ];

    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        if (settings.Enabled)
            await EnqueueRemindersAsync(cancellationToken);

        // Bound each pass so shutdown and other hosted services remain responsive.
        for (var i = 0; i < 50 && !cancellationToken.IsCancellationRequested; i++)
            if (!await DispatchOneAsync(cancellationToken)) break;
    }

    private async Task EnqueueRemindersAsync(CancellationToken cancellationToken)
    {
        var invoices = await (
            from invoice in db.Invoices.IgnoreQueryFilters().AsNoTracking()
            join customer in db.Customers.IgnoreQueryFilters().AsNoTracking()
                on new { invoice.OrganizationId, CustomerId = invoice.CustomerId }
                equals new { customer.OrganizationId, CustomerId = customer.Id }
            join organization in db.Organizations.AsNoTracking()
                on invoice.OrganizationId equals organization.Id
            where (invoice.Status == InvoiceStatus.Issued || invoice.Status == InvoiceStatus.PartiallyPaid)
                  && invoice.DueDate != null && invoice.AmountPaid < invoice.Total
                  && customer.Email != null && customer.Email != ""
                  && !customer.IsArchived && !organization.IsArchived
            select new ReminderInvoice(
                invoice.Id, invoice.OrganizationId, invoice.Number!, invoice.CurrencyCode,
                invoice.Total - invoice.AmountPaid, invoice.DueDate!.Value,
                customer.Name, customer.Email!, organization.Name, organization.Email,
                organization.TimeZoneId)
        ).ToListAsync(cancellationToken);

        var now = clock.GetUtcNow();
        foreach (var invoice in invoices)
        {
            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(invoice.TimeZoneId); }
            catch (TimeZoneNotFoundException ex)
            {
                logger.LogError(ex, "Skipping reminders for organization {OrganizationId}: invalid time zone", invoice.OrganizationId);
                continue;
            }
            catch (InvalidTimeZoneException ex)
            {
                logger.LogError(ex, "Skipping reminders for organization {OrganizationId}: invalid time zone", invoice.OrganizationId);
                continue;
            }

            var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
            foreach (var point in ReminderPoints)
            {
                if (localToday < invoice.DueDate.AddDays(point.OffsetDays)) continue;
                var message = EmailOutboxMessage.Create(
                    invoice.OrganizationId, $"reminder:{invoice.InvoiceId:N}:{point.Key}",
                    invoice.CustomerEmail, invoice.OrganizationEmail,
                    $"Payment reminder: invoice {invoice.Number} {point.Label}",
                    $"Hello {invoice.CustomerName},\n\nInvoice {invoice.Number} from {invoice.OrganizationName} {point.Label}.\nOutstanding balance: {invoice.CurrencyCode} {invoice.Balance.ToString("N2", CultureInfo.InvariantCulture)}.\n\nPlease contact {invoice.OrganizationName} if you have already paid.\n\nThank you,\n{invoice.OrganizationName}", now);

                // The unique key makes repeated scans and multiple worker instances idempotent.
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "EmailOutbox"
                        ("Id", "OrganizationId", "IdempotencyKey", "To", "ReplyTo", "Subject", "Body",
                         "AvailableAt", "ProcessingUntil", "SentAt", "Attempts", "LastError", "DeadLettered", "CreatedAt")
                    VALUES
                        ({message.Id}, {message.OrganizationId}, {message.IdempotencyKey}, {message.To}, {message.ReplyTo},
                         {message.Subject}, {message.Body}, {message.AvailableAt}, NULL, NULL, 0, NULL, FALSE, {now})
                    ON CONFLICT ("OrganizationId", "IdempotencyKey") DO NOTHING
                    """, cancellationToken);
            }
        }
    }

    private async Task<bool> DispatchOneAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var leaseUntil = now.AddMinutes(settings.LeaseMinutes);
        var ids = await db.Database.SqlQuery<Guid>($"""
            UPDATE "EmailOutbox"
            SET "ProcessingUntil" = {leaseUntil}, "Attempts" = "Attempts" + 1
            WHERE "Id" = (
                SELECT "Id" FROM "EmailOutbox"
                WHERE "SentAt" IS NULL AND NOT "DeadLettered" AND "AvailableAt" <= {now}
                  AND ("ProcessingUntil" IS NULL OR "ProcessingUntil" <= {now})
                ORDER BY "AvailableAt", "CreatedAt"
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            RETURNING "Id" AS "Value"
            """).ToListAsync(cancellationToken);
        if (ids.Count == 0) return false;

        var email = await db.EmailOutbox.IgnoreQueryFilters()
            .FirstAsync(x => x.Id == ids[0], cancellationToken);
        try
        {
            var accepted = await sender.SendAsync(
                new OutgoingEmail(email.To, email.Subject, email.Body, ReplyTo: email.ReplyTo), cancellationToken);
            if (!accepted) throw new InvalidOperationException("Email provider rejected the message.");
            email.MarkSent(clock.GetUtcNow());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var dead = email.Attempts >= settings.MaxAttempts;
            var delay = TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Min(email.Attempts, 6))));
            email.MarkFailed(ex.Message, clock.GetUtcNow().Add(delay), dead);
            logger.LogError(ex, "Email outbox message {MessageId} failed on attempt {Attempt}/{MaxAttempts}",
                email.Id, email.Attempts, settings.MaxAttempts);
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private sealed record ReminderInvoice(
        Guid InvoiceId, Guid OrganizationId, string Number, string CurrencyCode, decimal Balance,
        DateOnly DueDate, string CustomerName, string CustomerEmail, string OrganizationName,
        string? OrganizationEmail, string TimeZoneId);
}
