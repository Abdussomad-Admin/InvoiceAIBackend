using Application.Common;
using Application.Common.Interfaces;
using Domain.Users;
using System.Text.Encodings.Web;

namespace Application.Auth;

public sealed class AccountMailer(
    IUserTokenRepository tokens, ISecureTokenService secure, AppLinkSettings links, TimeProvider clock)
{
    private static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

    public async Task<OutgoingEmail> BuildVerificationAsync(User user, CancellationToken cancellationToken)
    {
        var token = await IssueAsync(user.Id, UserTokenPurpose.EmailVerification, VerificationLifetime, cancellationToken);
        var link = Link("verify-email", token);
        var safeName = HtmlEncoder.Default.Encode(user.FullName);
        var safeLink = HtmlEncoder.Default.Encode(link);
        var html = $"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:32px 16px;background:#f3f7fc;font-family:Arial,Helvetica,sans-serif;color:#14284a">
              <div style="max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #e1eaf5;border-radius:16px;overflow:hidden">
                <div style="padding:25px 30px;border-bottom:1px solid #edf1f7;font-size:22px;font-weight:700">Invoice <span style="color:#08b979">AI</span></div>
                <div style="padding:30px">
                  <h1 style="margin:0 0 16px;font-size:25px;line-height:1.25">Verify your email</h1>
                  <p style="margin:0 0 12px;font-size:15px;line-height:1.6">Hello {safeName},</p>
                  <p style="margin:0 0 24px;font-size:15px;line-height:1.6;color:#40536d">Confirm your email address to activate your account and continue to your dashboard.</p>
                  <p style="margin:0 0 26px"><a href="{safeLink}" style="display:inline-block;padding:13px 24px;border-radius:9px;background:#0874f8;color:#ffffff;text-decoration:none;font-size:15px;font-weight:700">Continue</a></p>
                  <p style="margin:0 0 8px;font-size:13px;color:#52657e">Or copy this link into your browser:</p>
                  <div style="padding:12px;border:1px solid #dce7f4;border-radius:8px;background:#f7faff;font-size:13px;line-height:1.6;overflow-wrap:anywhere;word-break:break-all;color:#075dc9">{safeLink}</div>
                  <p style="margin:22px 0 0;font-size:13px;color:#687b93">This link expires in 24 hours. If you didn't create an Invoice AI account, you can ignore this email.</p>
                </div>
              </div>
            </body>
            </html>
            """;
        return new OutgoingEmail(
            user.Email,
            "Verify your Invoice AI email",
            $"Hello {user.FullName},\n\nConfirm your email address to activate your account and continue to your dashboard.\n\nContinue: {link}\n\nOr copy this link into your browser:\n{link}\n\nThis link expires in 24 hours. If you didn't create an Invoice AI account, you can ignore this email.\n",
            HtmlBody: html);
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
