using Application.Common.Interfaces;
using Application.Organizations;
using Application.Users;
using Domain.Common;
using Domain.Users;

namespace Application.Auth;

public sealed class VerifyEmailHandler(
    IUserTokenRepository tokens, IUserRepository users, ISecureTokenService secure,
    IMembershipRepository memberships, SessionIssuer sessions,
    IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task<LoginResult> Handle(VerifyEmailCommand command, CancellationToken cancellationToken)
    {
        const string invalid = "This verification link is invalid or has expired.";
        var now = clock.GetUtcNow();

        var token = await tokens.GetByHashAsync(
            UserTokenPurpose.EmailVerification, secure.Hash(command.Token ?? ""), cancellationToken);
        if (token is null || !token.IsUsable(now)) throw new DomainException(invalid);

        var user = await users.GetByIdAsync(token.UserId, cancellationToken)
            ?? throw new DomainException(invalid);
        if (!user.IsActive) throw new DomainException(invalid);

        user.VerifyEmail(now);
        token.MarkUsed(now);
        var active = await memberships.GetActiveForUserAsync(user.Id, cancellationToken);
        var organizationId = active.OrderBy(m => m.CreatedAt)
            .Select(m => (Guid?)m.OrganizationId)
            .FirstOrDefault();
        var result = sessions.Issue(user, organizationId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }
}

public sealed class ResendVerificationHandler(
    IUserRepository users, AccountMailer mailer, IUnitOfWork unitOfWork, IEmailSender emailSender)
{
    public async Task Handle(ResendVerificationCommand command, CancellationToken cancellationToken)
    {
        string email;
        try { email = User.NormalizeEmail(command.Email); }
        catch (DomainException) { return; }

        var user = await users.GetByEmailAsync(email, cancellationToken);
        if (user is null || !user.IsActive || user.IsEmailVerified) return;

        var mail = await mailer.BuildVerificationAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await emailSender.SendAsync(mail, cancellationToken);
    }
}

public sealed class ForgotPasswordHandler(
    IUserRepository users, AccountMailer mailer, IUnitOfWork unitOfWork, IEmailSender emailSender)
{
    public async Task Handle(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        string email;
        try { email = User.NormalizeEmail(command.Email); }
        catch (DomainException) { return; }

        var user = await users.GetByEmailAsync(email, cancellationToken);
        if (user is null || !user.IsActive) return;

        var mail = await mailer.BuildPasswordResetAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await emailSender.SendAsync(mail, cancellationToken);
    }
}

public sealed class ResetPasswordHandler(
    IUserTokenRepository tokens, IUserRepository users, ISecureTokenService secure,
    IPasswordHasher passwordHasher, IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        const string invalid = "This reset link is invalid or has expired.";
        PasswordPolicy.Validate(command.NewPassword);
        var now = clock.GetUtcNow();

        var token = await tokens.GetByHashAsync(
            UserTokenPurpose.PasswordReset, secure.Hash(command.Token ?? ""), cancellationToken);
        if (token is null || !token.IsUsable(now)) throw new DomainException(invalid);

        var user = await users.GetByIdAsync(token.UserId, cancellationToken)
            ?? throw new DomainException(invalid);

        user.ChangePasswordHash(passwordHasher.Hash(command.NewPassword));
        token.MarkUsed(now);

        foreach (var session in await refreshTokens.GetActiveForUserAsync(user.Id, cancellationToken))
            session.Revoke(now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
