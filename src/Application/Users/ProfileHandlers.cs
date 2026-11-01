using Application.Auth;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Domain.Common;
using Domain.Users;

namespace Application.Users;

public sealed record ProfileDto(
    Guid UserId, string Email, string FullName, bool EmailVerified,
    DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);

public sealed record UpdateProfileCommand(string FullName);
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword);

internal static class ProfileMapping
{
    public static ProfileDto ToDto(this User u)
        => new(u.Id, u.Email, u.FullName, u.IsEmailVerified, u.CreatedAt, u.LastLoginAt);
}

public sealed class GetProfileHandler(IUserRepository users, ICurrentUser currentUser)
{
    public async Task<ProfileDto> Handle(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");
        var user = await users.GetByIdAsync(userId, cancellationToken)
            ?? throw new AuthenticationFailedException("Not signed in.");
        return user.ToDto();
    }
}

public sealed class UpdateProfileHandler(IUserRepository users, IUnitOfWork unitOfWork, ICurrentUser currentUser)
{
    public async Task<ProfileDto> Handle(UpdateProfileCommand command, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");
        var user = await users.GetByIdAsync(userId, cancellationToken)
            ?? throw new AuthenticationFailedException("Not signed in.");

        user.UpdateName(command.FullName);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return user.ToDto();
    }
}

public sealed class ChangePasswordHandler(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher passwordHasher,
    SessionIssuer sessions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ICurrentOrganization currentOrganization,
    TimeProvider clock)
{
    public async Task<LoginResult> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");
        var user = await users.GetByIdAsync(userId, cancellationToken)
            ?? throw new AuthenticationFailedException("Not signed in.");

        // 400, not 401: a wrong current password is a bad request, not an expired session.
        if (!passwordHasher.Verify(user.PasswordHash, command.CurrentPassword ?? ""))
            throw new DomainException("Current password is incorrect.");

        PasswordPolicy.Validate(command.NewPassword);
        if (command.NewPassword == command.CurrentPassword)
            throw new DomainException("Choose a password different from the current one.");

        var now = clock.GetUtcNow();
        user.ChangePasswordHash(passwordHasher.Hash(command.NewPassword));

        // Every existing session ends, including any held by someone who knew the old password.
        foreach (var session in await refreshTokens.GetActiveForUserAsync(user.Id, cancellationToken))
            session.Revoke(now);

        // This device gets a fresh session so the user stays signed in.
        var result = sessions.Issue(user, currentOrganization.OrganizationId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }
}