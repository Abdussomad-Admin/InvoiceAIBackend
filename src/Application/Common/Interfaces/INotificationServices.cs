namespace Application.Common.Interfaces;

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

public sealed record OutgoingEmail(
    string To, string Subject, string Body,
    IReadOnlyList<EmailAttachment>? Attachments = null,
    string? ReplyTo = null,
    string? HtmlBody = null);

public interface IEmailSender
{
    // True if the email was accepted for delivery, false if it could not be sent.
    // Account emails ignore the result on purpose, so nothing reveals which accounts exist.
    Task<bool> SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default);
}

public interface ISecureTokenService
{
    string Generate();
    string Hash(string token);
}
