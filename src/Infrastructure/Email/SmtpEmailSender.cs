using Application.Common.Interfaces;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Infrastructure.Email;

public sealed class SmtpEmailSender(EmailSettings settings, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
            message.To.Add(MailboxAddress.Parse(email.To));
            message.Subject = email.Subject;
            message.Body = new TextPart("plain") { Text = email.Body };

            using var client = new SmtpClient { Timeout = settings.TimeoutSeconds * 1000 };
            await client.ConnectAsync(settings.Host, settings.Port, settings.SocketOptions, cancellationToken);

            if (!string.IsNullOrEmpty(settings.Username))
                await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            logger.LogInformation("Email sent with subject {Subject}", email.Subject);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Deliberately not rethrown (see the design note). Never log the body: it holds secret links.
            logger.LogError(ex, "Failed to send email with subject {Subject}", email.Subject);
        }
    }
}