using Application.Auth;
using Application.Common.Exceptions;
using Application.Organizations;
using Application.Users;
using Domain.Common;
using Infrastructure.Auth;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public class ProfileTests
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

    private const string Password = "Passw0rd!23";

    private static async Task<(string Email, RegisterResult Result)> RegisterAsync()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        var sender = new CapturingEmailSender();
        await using var ctx = TestDatabase.CreateContext(Clock);
        var handler = new RegisterHandler(
            new UserRepository(ctx), new OrganizationRepository(ctx), new MembershipRepository(ctx),
            ctx, new IdentityPasswordHasher(), AccountTestHelpers.CreateMailer(ctx, Clock),
            sender);
        var result = await handler.Handle(
            new RegisterCommand(email, Password, "Ada Obi", $"Acme {Guid.NewGuid()}"),
            CancellationToken.None);

        await using var verificationContext = TestDatabase.CreateContext(Clock);
        var verification = new VerifyEmailHandler(
            new UserTokenRepository(verificationContext), new UserRepository(verificationContext),
            new SecureTokenService(), new MembershipRepository(verificationContext),
            AccountTestHelpers.CreateSessions(verificationContext, Clock, Jwt), verificationContext, Clock);
        await verification.Handle(new VerifyEmailCommand(sender.LastTokenFor(email)), CancellationToken.None);

        return (email, result);
    }

    private static async Task<LoginResult> LoginAsync(string email, string password = Password)
    {
        await using var ctx = TestDatabase.CreateContext(Clock);
        var handler = new LoginHandler(
            new UserRepository(ctx), new MembershipRepository(ctx), ctx,
            new IdentityPasswordHasher(), AccountTestHelpers.CreateSessions(ctx, Clock, Jwt), Clock,
            AccountTestHelpers.CreateMailer(ctx, Clock), new CapturingEmailSender());
        return await handler.Handle(new LoginCommand(email, password), CancellationToken.None);
    }

    private static async Task<LoginResult> RefreshAsync(string refreshToken)
    {
        await using var ctx = TestDatabase.CreateContext(Clock);
        var handler = new RefreshHandler(
            new RefreshTokenRepository(ctx), new UserRepository(ctx), new MembershipRepository(ctx),
            new SecureTokenService(), AccountTestHelpers.CreateSessions(ctx, Clock, Jwt), ctx, Clock);
        return await handler.Handle(new RefreshCommand(refreshToken), CancellationToken.None);
    }

    private static async Task<LoginResult> ChangePasswordAsync(
        RegisterResult user, string current, string next)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, user.OrganizationId);
        var handler = new ChangePasswordHandler(
            new UserRepository(ctx), new RefreshTokenRepository(ctx), new IdentityPasswordHasher(),
            AccountTestHelpers.CreateSessions(ctx, Clock, Jwt), ctx,
            new TestCurrentUser(user.UserId), new TestCurrentOrganization(user.OrganizationId), Clock);
        return await handler.Handle(new ChangePasswordCommand(current, next), CancellationToken.None);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_Throws()
    {
        var (_, user) = await RegisterAsync();

        await Assert.ThrowsAsync<DomainException>(
            () => ChangePasswordAsync(user, "not-my-password", "NewPassw0rd!99"));
    }

    [Fact]
    public async Task ChangePassword_ToTheSamePassword_Throws()
    {
        var (_, user) = await RegisterAsync();

        await Assert.ThrowsAsync<DomainException>(
            () => ChangePasswordAsync(user, Password, Password));
    }

    [Fact]
    public async Task ChangePassword_NewPasswordWorks_AndOldOneDoesNot()
    {
        var (email, user) = await RegisterAsync();

        await ChangePasswordAsync(user, Password, "NewPassw0rd!99");

        await LoginAsync(email, "NewPassw0rd!99");
        await Assert.ThrowsAsync<AuthenticationFailedException>(() => LoginAsync(email, Password));
    }

    [Fact]
    public async Task ChangePassword_EndsOldSessions_ButKeepsTheCurrentDeviceSignedIn()
    {
        var (email, user) = await RegisterAsync();
        var oldSession = await LoginAsync(email);

        var newSession = await ChangePasswordAsync(user, Password, "NewPassw0rd!99");

        // Check the new session first: presenting a revoked token ends every session.
        var refreshed = await RefreshAsync(newSession.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.Token));

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => RefreshAsync(oldSession.RefreshToken));
    }

    [Fact]
    public async Task UpdateProfile_ChangesTheName()
    {
        var (_, user) = await RegisterAsync();

        await using var ctx = TestDatabase.CreateContext(Clock);
        var updated = await new UpdateProfileHandler(
                new UserRepository(ctx), ctx, new TestCurrentUser(user.UserId))
            .Handle(new UpdateProfileCommand("  Ada Lovelace  "), CancellationToken.None);

        Assert.Equal("Ada Lovelace", updated.FullName);

        await using var readCtx = TestDatabase.CreateContext(Clock);
        var profile = await new GetProfileHandler(
                new UserRepository(readCtx), new TestCurrentUser(user.UserId))
            .Handle(CancellationToken.None);
        Assert.Equal("Ada Lovelace", profile.FullName);
    }
}

public class OrganizationProfileTests
{
    private static readonly FixedTimeProvider Clock =
        new(new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero));

    private static async Task<RegisterResult> RegisterAsync()
    {
        await using var ctx = TestDatabase.CreateContext(Clock);
        var handler = new RegisterHandler(
            new UserRepository(ctx), new OrganizationRepository(ctx), new MembershipRepository(ctx),
            ctx, new IdentityPasswordHasher(), AccountTestHelpers.CreateMailer(ctx, Clock),
            new CapturingEmailSender());
        return await handler.Handle(
            new RegisterCommand($"{Guid.NewGuid()}@example.com", "Passw0rd!23", "Ada Obi", $"Acme {Guid.NewGuid()}"),
            CancellationToken.None);
    }

    private static UpdateOrganizationInput Input(
        string name = "Acme Trading Ltd", string? email = "billing@acme.com", int terms = 14)
        => new(name, email, "+2348012345678", "TIN-12345", "1 Marina Road", null, "Lagos", "Lagos", "100001", terms);

    private static async Task<OrganizationDto> UpdateAsync(Guid? organizationId, UpdateOrganizationInput input)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, organizationId);
        var handler = new UpdateOrganizationHandler(
            new OrganizationRepository(ctx), ctx, new TestCurrentOrganization(organizationId));
        return await handler.Handle(input, CancellationToken.None);
    }

    [Fact]
    public async Task UpdateOrganization_SavesTheDetails()
    {
        var owner = await RegisterAsync();

        await UpdateAsync(owner.OrganizationId, Input());

        await using var ctx = TestDatabase.CreateContext(Clock, owner.OrganizationId);
        var read = await new GetOrganizationHandler(
                new OrganizationRepository(ctx), new TestCurrentOrganization(owner.OrganizationId))
            .Handle(CancellationToken.None);

        Assert.Equal("Acme Trading Ltd", read.Name);
        Assert.Equal("billing@acme.com", read.Email);
        Assert.Equal("TIN-12345", read.TaxIdentificationNumber);
        Assert.Equal(14, read.DefaultPaymentTermsDays);
        Assert.Equal("NGN", read.BaseCurrencyCode);
    }

    [Fact]
    public async Task UpdateOrganization_WithInvalidEmail_Throws()
    {
        var owner = await RegisterAsync();

        await Assert.ThrowsAsync<DomainException>(
            () => UpdateAsync(owner.OrganizationId, Input(email: "not-an-email")));
    }

    [Fact]
    public async Task UpdateOrganization_WithPaymentTermsOutOfRange_Throws()
    {
        var owner = await RegisterAsync();

        await Assert.ThrowsAsync<DomainException>(
            () => UpdateAsync(owner.OrganizationId, Input(terms: 400)));
    }

    [Fact]
    public async Task UpdateOrganization_WithoutACurrentOrganization_IsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => UpdateAsync(null, Input()));
    }

    [Fact]
    public async Task UpdateOrganization_NeverTouchesAnotherOrganization()
    {
        var a = await RegisterAsync();
        var b = await RegisterAsync();

        await UpdateAsync(a.OrganizationId, Input(name: "Only Org A Changes"));

        await using var ctx = TestDatabase.CreateContext(Clock);
        var orgB = await ctx.Organizations.SingleAsync(o => o.Id == b.OrganizationId);
        Assert.NotEqual("Only Org A Changes", orgB.Name);
    }
}
