using Application.Auth;
using Application.Common.Exceptions;
using Domain.Organizations;
using Infrastructure.Auth;
using Infrastructure.Persistence.Repositories;

namespace Tests;

public class SessionTests
{
    private static readonly FixedTimeProvider Clock =
        new(new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero));

    private static readonly JwtSettings Jwt = new()
    {
        Issuer = "test",
        Audience = "test",
        Key = new string('k', 48),
        ExpiryMinutes = 15,
        RefreshTokenDays = 30
    };

    private static async Task<(string Email, RegisterResult Registered)> RegisterAsync()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var sender = new CapturingEmailSender();
        await using var ctx = TestDatabase.CreateContext(Clock);
        var handler = new RegisterHandler(
            new UserRepository(ctx), new OrganizationRepository(ctx), new MembershipRepository(ctx),
            ctx, new IdentityPasswordHasher(), AccountTestHelpers.CreateMailer(ctx, Clock),
            sender);
        var registered = await handler.Handle(
            new RegisterCommand(email, "Passw0rd!23", "Ada Obi", $"Acme {Guid.NewGuid()}"),
            CancellationToken.None);

        var verify = new VerifyEmailHandler(
            new UserTokenRepository(ctx), new UserRepository(ctx), new SecureTokenService(),
            new MembershipRepository(ctx), AccountTestHelpers.CreateSessions(ctx, Clock, Jwt), ctx, Clock);
        await verify.Handle(new VerifyEmailCommand(sender.LastTokenFor(email)), CancellationToken.None);

        return (email, registered);
    }

    private static async Task<LoginResult> LoginAsync(string email)
    {
        await using var ctx = TestDatabase.CreateContext(Clock);
        var handler = new LoginHandler(
            new UserRepository(ctx), new MembershipRepository(ctx), ctx,
            new IdentityPasswordHasher(), AccountTestHelpers.CreateSessions(ctx, Clock, Jwt), Clock,
            AccountTestHelpers.CreateMailer(ctx, Clock), new CapturingEmailSender());
        return await handler.Handle(new LoginCommand(email, "Passw0rd!23"), CancellationToken.None);
    }

    private static async Task<LoginResult> RefreshAsync(
        string refreshToken, Guid? organizationId = null, TimeProvider? clock = null)
    {
        clock ??= Clock;
        await using var ctx = TestDatabase.CreateContext(clock);
        var handler = new RefreshHandler(
            new RefreshTokenRepository(ctx), new UserRepository(ctx), new MembershipRepository(ctx),
            new SecureTokenService(), AccountTestHelpers.CreateSessions(ctx, clock, Jwt), ctx, clock);
        return await handler.Handle(new RefreshCommand(refreshToken, organizationId), CancellationToken.None);
    }

    private static async Task LogoutAsync(string refreshToken)
    {
        await using var ctx = TestDatabase.CreateContext(Clock);
        var handler = new LogoutHandler(
            new RefreshTokenRepository(ctx), new SecureTokenService(), ctx, Clock);
        await handler.Handle(new LogoutCommand(refreshToken), CancellationToken.None);
    }

    [Fact]
    public async Task Refresh_RotatesTheToken_AndReuseEndsEverySession()
    {
        var (email, _) = await RegisterAsync();
        var first = await LoginAsync(email);

        var second = await RefreshAsync(first.RefreshToken);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => RefreshAsync(first.RefreshToken));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => RefreshAsync(second.RefreshToken));
    }

    [Fact]
    public async Task Logout_RevokesTheRefreshToken()
    {
        var (email, _) = await RegisterAsync();
        var session = await LoginAsync(email);

        await LogoutAsync(session.RefreshToken);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => RefreshAsync(session.RefreshToken));
    }

    [Fact]
    public async Task Refresh_WithAnotherUsersOrganization_IsForbidden()
    {
        var (emailA, _) = await RegisterAsync();
        var (_, registeredB) = await RegisterAsync();
        var sessionA = await LoginAsync(emailA);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => RefreshAsync(sessionA.RefreshToken, registeredB.OrganizationId));
    }

    [Fact]
    public async Task Refresh_CanSwitchToAnotherOrganizationTheUserBelongsTo()
    {
        var (email, registered) = await RegisterAsync();

        Guid secondOrganizationId;
        await using (var ctx = TestDatabase.CreateContext(Clock))
        {
            var second = Organization.Create($"Second {Guid.NewGuid()}", "NG", "NGN", "Africa/Lagos");
            ctx.Organizations.Add(second);
            ctx.Memberships.Add(Membership.Create(registered.UserId, second.Id, OrganizationRole.Staff));
            await ctx.SaveChangesAsync();
            secondOrganizationId = second.Id;
        }

        var session = await LoginAsync(email);
        var switched = await RefreshAsync(session.RefreshToken, secondOrganizationId);

        Assert.Equal(secondOrganizationId, switched.OrganizationId);
    }

    [Fact]
    public async Task Refresh_AfterThirtyOneDays_Throws()
    {
        var (email, _) = await RegisterAsync();
        var session = await LoginAsync(email);
        var later = new FixedTimeProvider(Clock.GetUtcNow().AddDays(31));

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => RefreshAsync(session.RefreshToken, clock: later));
    }

    [Fact]
    public async Task ResettingThePassword_EndsExistingSessions()
    {
        var (email, _) = await RegisterAsync();
        var session = await LoginAsync(email);

        var sender = new CapturingEmailSender();
        await using (var ctx = TestDatabase.CreateContext(Clock))
        {
            await new ForgotPasswordHandler(
                    new UserRepository(ctx), AccountTestHelpers.CreateMailer(ctx, Clock), ctx, sender)
                .Handle(new ForgotPasswordCommand(email), CancellationToken.None);
        }

        var token = sender.LastTokenFor(email);
        await using (var ctx = TestDatabase.CreateContext(Clock))
        {
            await new ResetPasswordHandler(
                    new UserTokenRepository(ctx), new UserRepository(ctx), new SecureTokenService(),
                    new IdentityPasswordHasher(), new RefreshTokenRepository(ctx), ctx, Clock)
                .Handle(new ResetPasswordCommand(token, "NewPassw0rd!99"), CancellationToken.None);
        }

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => RefreshAsync(session.RefreshToken));
    }
}
