using Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Email;

public sealed class DevelopmentEmailSender(ILogger<DevelopmentEmailSender> logger) : IEmailSender
{
    public Task<bool> SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        var attachments = email.Attachments is { Count: > 0 } list
            ? string.Join(", ", list.Select(a => $"{a.FileName} ({a.Content.Length} bytes)"))
            : "none";

        logger.LogWarning(
            "DEVELOPMENT EMAIL (not actually sent)\nTo: {To}\nSubject: {Subject}\nAttachments: {Attachments}\n{Body}",
            email.To, email.Subject, attachments, email.Body);

        return Task.FromResult(true);
    }
}