namespace Application.Common.Interfaces
{
    public sealed record OutgoingEmail(string To, string Subject, string Body);

    public interface IEmailSender
    {
        Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default);
    }

    public interface ISecureTokenService
    {
        string Generate();
        string Hash(string token);
    }
}