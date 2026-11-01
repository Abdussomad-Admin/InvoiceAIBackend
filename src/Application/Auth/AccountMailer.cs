using Application.Common;
using Application.Common.Interfaces;
using Domain.Users;

namespace Application.Auth;

public sealed class AccountMailer(
    IUserTokenRepository tokens, ISecureTokenService secure, AppLinkSettings links, TimeProvider clock)
{
    private static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

    public async Task<OutgoingEmail> BuildVerificationAsync(User user, CancellationToken cancellationToken)
    {
        var token = await IssueAsync(user.Id, UserTokenPurpose.EmailVerification, VerificationLifetime, cancellationToken);
        return new OutgoingEmail(
            user.Email,
            "Verify your Invoice AI email",
            $"Hello {user.FullName},\n\nPlease confirm your email address:\n{Link("verify-email", token)}\n\nThis link expires in 24 hours.\n");
    }

    public async Task<OutgoingEmail> BuildPasswordResetAsync(User user, CancellationToken cancellationToken)
    {
        var token = await IssueAsync(user.Id, UserTokenPurpose.PasswordReset, ResetLifetime, cancellationToken);
        return new OutgoingEmail(
            user.Email,
            "Reset your Invoice AI password",
            $"Hello {user.FullName},\n\nUse this link to choose a new password:\n{Link("reset-password", token)}\n\nThis link expires in 1 hour. If you did not ask for this, ignore this email.\n");
    }

    private async Task<string> IssueAsync(
        Guid userId, UserTokenPurpose purpose, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        // Only the newest link should work.
        foreach (var old in await tokens.GetUnusedAsync(userId, purpose, cancellationToken))
            old.MarkUsed(now);

        var raw = secure.Generate();
        tokens.Add(UserToken.Create(userId, purpose, secure.Hash(raw), now.Add(lifetime)));
        return raw;
    }

    private string Link(string page, string token)
        => $"{links.FrontendBaseUrl.TrimEnd('/')}/{page}?token={token}";
}