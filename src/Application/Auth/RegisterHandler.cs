using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Organizations;
using Application.Users;
using Domain.Organizations;
using Domain.Users;

namespace Application.Auth;

public sealed class RegisterHandler(
    IUserRepository users,
    IOrganizationRepository organizations,
    IMembershipRepository memberships,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    AccountMailer mailer,
    IEmailSender emailSender)
{
    public async Task<RegisterResult> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        PasswordPolicy.Validate(command.Password);

        var email = User.NormalizeEmail(command.Email);
        var existingUser = await users.GetByEmailAsync(email, cancellationToken);
        if (existingUser is not null)
        {
            if (!existingUser.IsEmailVerified && existingUser.IsActive)
            {
                var existingMemberships = await memberships.GetActiveForUserAsync(existingUser.Id, cancellationToken);
                var existingOrganizationId = existingMemberships
                    .OrderBy(m => m.CreatedAt)
                    .Select(m => (Guid?)m.OrganizationId)
                    .FirstOrDefault();
                if (existingOrganizationId is null)
                    throw new ConflictException("This account needs support before it can be activated.");

                var resend = await mailer.BuildVerificationAsync(existingUser, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                await emailSender.SendAsync(resend, cancellationToken);
                return new RegisterResult(existingUser.Id, existingOrganizationId.Value);
            }

            throw new ConflictException("An account with this email already exists.");
        }

        var user = User.Register(email, passwordHasher.Hash(command.Password), command.FullName);
        var organization = Organization.Create(
            command.OrganizationName, command.CountryCode, command.CurrencyCode, command.TimeZoneId);
        var membership = Membership.Create(user.Id, organization.Id, OrganizationRole.Owner);

        users.Add(user);
        organizations.Add(organization);
        memberships.Add(membership);
        var verification = await mailer.BuildVerificationAsync(user, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await emailSender.SendAsync(verification, cancellationToken);

        return new RegisterResult(user.Id, organization.Id);
    }
}
