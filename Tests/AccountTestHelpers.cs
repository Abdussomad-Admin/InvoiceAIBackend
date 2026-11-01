using Application.Auth;
using Application.Common;
using Application.Common.Interfaces;
using Infrastructure.Auth;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;

namespace Tests;

public sealed class CapturingEmailSender : IEmailSender
{
    public List<OutgoingEmail> Sent { get; } = [];

    public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        Sent.Add(email);
        return Task.CompletedTask;
    }

    public string LastTokenFor(string toEmail)
        => Sent.Last(e => e.To == toEmail).Body.Split("token=")[1].Split('\n', '\r', ' ')[0];
}

public static class AccountTestHelpers
{
    public static AccountMailer CreateMailer(AppDbContext context, TimeProvider clock)
        => new(new UserTokenRepository(context), new SecureTokenService(),
               new AppLinkSettings("http://localhost:5173"), clock);

    public static SessionIssuer CreateSessions(AppDbContext context, TimeProvider clock, JwtSettings jwt)
        => new(new JwtTokenService(jwt, clock), new SecureTokenService(),
               new RefreshTokenRepository(context), clock);
}