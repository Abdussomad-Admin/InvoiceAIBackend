using Application.Auth;
using Application.Common.Exceptions;
using Domain.Common;
using Infrastructure.Auth;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public class AuthTests
{
    private static readonly FixedTimeProvider Clock =
        new(new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero));

    private static readonly JwtSettings Jwt = new()
    {
        Issuer = "test",
        Audience = "test",
        Key = new string('k', 48),
        ExpiryMinutes = 60
    };

    private static async Task<RegisterResult> RegisterAsync(
        RegisterCommand command, CapturingEmailSender? sender = null)
    {
        sender ??= new CapturingEmailSender();
        await using var ctx = TestDatabase.CreateContext(Clock);
        var handler = new RegisterHandler(
            new UserRepository(ctx), new OrganizationRepository(ctx), new MembershipRepository(ctx),
            ctx, new IdentityPasswordHasher(), AccountTestHelpers.CreateMailer(ctx, Clock), sender);
        return await handler.Handle(command, CancellationToken.None);
    }

    private static async Task<LoginResult> LoginAsync(LoginCommand command, CapturingEmailSender? sender = null)
    {
        sender ??= new CapturingEmailSender();
        await using var ctx = TestDatabase.CreateContext(Clock);
        var handler = new LoginHandler(
            new UserRepository(ctx), new MembershipRepository(ctx), ctx,
            new IdentityPasswordHasher(), AccountTestHelpers.CreateSessions(ctx, Clock, Jwt), Clock,
            AccountTestHelpers.CreateMailer(ctx, Clock), sender);
        return await handler.Handle(command, CancellationToken.None);
    }

    private static async Task VerifyAsync(string token, TimeProvider clock)
    {
        await using var ctx = TestDatabase.CreateContext(clock);
        var handler = new VerifyEmailHandler(
            new UserTokenRepository(ctx), new UserRepository(ctx), new SecureTokenService(),
            new MembershipRepository(ctx), AccountTestHelpers.CreateSessions(ctx, clock, Jwt), ctx, clock);
        await handler.Handle(new VerifyEmailCommand(token), CancellationToken.None);
    }

    private static async Task ForgotAsync(string email, CapturingEmailSender sender)
    {
        await using var ctx = TestDatabase.CreateContext(Clock);
        var handler = new ForgotPasswordHandler(
            new UserRepository(ctx), AccountTestHelpers.CreateMailer(ctx, Clock), ctx, sender);
        await handler.Handle(new ForgotPasswordCommand(email), CancellationToken.None);
    }

    private static async Task ResetAsync(string token, string newPassword, TimeProvider clock)
    {
        await using var ctx = TestDatabase.CreateContext(clock);
        var handler = new ResetPasswordHandler(
            new UserTokenRepository(ctx), new UserRepository(ctx), new SecureTokenService(),
            new IdentityPasswordHasher(), new RefreshTokenRepository(ctx), ctx, clock);
        await handler.Handle(new ResetPasswordCommand(token, newPassword), CancellationToken.None);
    }

    private static RegisterCommand NewRegistration(string email) =>
        new(email, "Passw0rd!23", "Ada Obi", $"Acme {Guid.NewGuid()}");

    [Fact]
    public async Task RegisterThenLogin_ReturnsTokenForTheNewOrganization()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var sender = new CapturingEmailSender();
        var registered = await RegisterAsync(NewRegistration(email), sender);
        var loginSender = new CapturingEmailSender();

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => LoginAsync(new LoginCommand(email, "Passw0rd!23"), loginSender));

        await VerifyAsync(loginSender.LastTokenFor(email), Clock);

        var result = await LoginAsync(new LoginCommand(email, "Passw0rd!23"));

        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal(registered.OrganizationId, result.OrganizationId);
    }

    [Fact]
    public async Task Register_WithExistingUnverifiedEmail_ResendsVerificationForTheSameAccount()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var firstSender = new CapturingEmailSender();
        var first = await RegisterAsync(NewRegistration(email), firstSender);
        var resendSender = new CapturingEmailSender();

        var second = await RegisterAsync(NewRegistration(email), resendSender);

        Assert.Equal(first.UserId, second.UserId);
        Assert.Equal(first.OrganizationId, second.OrganizationId);
        Assert.NotEqual(firstSender.LastTokenFor(email), resendSender.LastTokenFor(email));
    }

    [Fact]
    public async Task Register_WithShortPassword_Throws()
    {
        var command = NewRegistration($"{Guid.NewGuid()}@example.com") with { Password = "short" };

        await Assert.ThrowsAsync<DomainException>(() => RegisterAsync(command));
    }

    [Fact]
    public async Task Login_WithWrongPassword_Throws()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        await RegisterAsync(NewRegistration(email));

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => LoginAsync(new LoginCommand(email, "wrong-password")));
    }

    [Fact]
    public async Task VerifyEmail_MarksUserVerified_AndTokenWorksOnlyOnce()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var sender = new CapturingEmailSender();
        await RegisterAsync(NewRegistration(email), sender);
        var token = sender.LastTokenFor(email);

        await VerifyAsync(token, Clock);

        await using var ctx = TestDatabase.CreateContext(Clock);
        var user = await ctx.Users.SingleAsync(u => u.Email == email);
        Assert.True(user.IsEmailVerified);

        await Assert.ThrowsAsync<DomainException>(() => VerifyAsync(token, Clock));
    }

    [Fact]
    public async Task VerifyEmail_WithExpiredToken_Throws()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var sender = new CapturingEmailSender();
        await RegisterAsync(NewRegistration(email), sender);
        var token = sender.LastTokenFor(email);

        var twoDaysLater = new FixedTimeProvider(Clock.GetUtcNow().AddDays(2));

        await Assert.ThrowsAsync<DomainException>(() => VerifyAsync(token, twoDaysLater));
    }

    [Fact]
    public async Task ForgotPassword_ForUnknownEmail_SendsNothingAndDoesNotThrow()
    {
        var sender = new CapturingEmailSender();

        await ForgotAsync($"{Guid.NewGuid()}@example.com", sender);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task ResetPassword_ChangesThePassword_AndTokenWorksOnlyOnce()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var registrationSender = new CapturingEmailSender();
        await RegisterAsync(NewRegistration(email), registrationSender);
        await VerifyAsync(registrationSender.LastTokenFor(email), Clock);

        var sender = new CapturingEmailSender();
        await ForgotAsync(email, sender);
        var token = sender.LastTokenFor(email);

        await ResetAsync(token, "NewPassw0rd!99", Clock);

        await LoginAsync(new LoginCommand(email, "NewPassw0rd!99"));
        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => LoginAsync(new LoginCommand(email, "Passw0rd!23")));
        await Assert.ThrowsAsync<DomainException>(() => ResetAsync(token, "Another!Pass1", Clock));
    }
}
