using Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Email;

public sealed class DevelopmentEmailSender(ILogger<DevelopmentEmailSender> logger) : IEmailSender
{
    public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            "DEVELOPMENT EMAIL (not actually sent)\nTo: {To}\nSubject: {Subject}\n{Body}",
            email.To, email.Subject, email.Body);
        return Task.CompletedTask;
    }
}