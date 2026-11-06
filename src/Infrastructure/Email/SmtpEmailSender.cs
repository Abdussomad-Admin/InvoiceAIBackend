using Application.Common.Interfaces;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Infrastructure.Email;

public sealed class SmtpEmailSender(EmailSettings settings, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public static MimeMessage BuildMessage(EmailSettings settings, OutgoingEmail email)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(email.To));
        if (!string.IsNullOrWhiteSpace(email.ReplyTo))
            message.ReplyTo.Add(MailboxAddress.Parse(email.ReplyTo));
        message.Subject = email.Subject;

        var builder = new BodyBuilder { TextBody = email.Body };
        foreach (var attachment in email.Attachments ?? Array.Empty<EmailAttachment>())
            builder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));

        message.Body = builder.ToMessageBody();
        return message;
    }

    public async Task<bool> SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = BuildMessage(settings, email);

            using var client = new SmtpClient { Timeout = settings.TimeoutSeconds * 1000 };
            await client.ConnectAsync(settings.Host, settings.Port, settings.SocketOptions, cancellationToken);

            if (!string.IsNullOrEmpty(settings.Username))
                await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            logger.LogInformation("Email sent with subject {Subject}", email.Subject);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Not rethrown: callers that must know use the return value; account emails ignore it.
            // Never log the body: it can hold secret links.
            logger.LogError(ex, "Failed to send email with subject {Subject}", email.Subject);
            return false;
        }
    }
}