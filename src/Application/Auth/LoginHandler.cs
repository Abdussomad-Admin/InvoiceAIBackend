using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Organizations;
using Application.Users;
using Domain.Common;
using Domain.Users;

namespace Application.Auth;

public sealed class LoginHandler(
    IUserRepository users,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    SessionIssuer sessions,
    TimeProvider clock,
    AccountMailer mailer,
    IEmailSender emailSender)
{
    private const string InvalidCredentials = "Invalid email or password.";

    public async Task<LoginResult> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        string email;
        try { email = User.NormalizeEmail(command.Email); }
        catch (DomainException) { throw new AuthenticationFailedException(InvalidCredentials); }

        var user = await users.GetByEmailAsync(email, cancellationToken);

        if (user is null || !user.IsActive || !passwordHasher.Verify(user.PasswordHash, command.Password ?? ""))
            throw new AuthenticationFailedException(InvalidCredentials);

        if (!user.IsEmailVerified)
        {
            var verification = await mailer.BuildVerificationAsync(user, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await emailSender.SendAsync(verification, cancellationToken);
            throw new AuthenticationFailedException(
                "Your email is not verified yet. We sent a fresh verification link. Open it to continue to your dashboard.");
        }

        var active = await memberships.GetActiveForUserAsync(user.Id, cancellationToken);
        Guid? organizationId = active
            .OrderBy(m => m.CreatedAt)
            .Select(m => (Guid?)m.OrganizationId)
            .FirstOrDefault();

        user.RecordLogin(clock.GetUtcNow());
        var result = sessions.Issue(user, organizationId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }
}
