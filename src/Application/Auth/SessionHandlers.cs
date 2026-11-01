using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Organizations;
using Application.Users;

namespace Application.Auth;

public sealed class RefreshHandler(
    IRefreshTokenRepository refreshTokens,
    IUserRepository users,
    IMembershipRepository memberships,
    ISecureTokenService secure,
    SessionIssuer sessions,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<LoginResult> Handle(RefreshCommand command, CancellationToken cancellationToken)
    {
        const string invalid = "Your session has expired. Please log in again.";
        var now = clock.GetUtcNow();

        var stored = await refreshTokens.GetByHashAsync(secure.Hash(command.RefreshToken ?? ""), cancellationToken) ?? throw new AuthenticationFailedException(invalid);
        if (stored.RevokedAt is not null)
        {
            foreach (var token in await refreshTokens.GetActiveForUserAsync(stored.UserId, cancellationToken))
                token.Revoke(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new AuthenticationFailedException(invalid);
        }

        if (!stored.IsActive(now)) throw new AuthenticationFailedException(invalid);

        var user = await users.GetByIdAsync(stored.UserId, cancellationToken);
        if (user is null || !user.IsActive) throw new AuthenticationFailedException(invalid);

        var active = await memberships.GetActiveForUserAsync(user.Id, cancellationToken);

        if (command.OrganizationId is { } requested && active.All(m => m.OrganizationId != requested))
            throw new ForbiddenException("You are not a member of that organization.");

        Guid? wanted = command.OrganizationId ?? stored.OrganizationId;
        Guid? organizationId = active.Any(m => m.OrganizationId == wanted)
            ? wanted
            : active.OrderBy(m => m.CreatedAt).Select(m => (Guid?)m.OrganizationId).FirstOrDefault();

        stored.Revoke(now);
        var result = sessions.Issue(user, organizationId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }
}

public sealed class LogoutHandler(
    IRefreshTokenRepository refreshTokens, ISecureTokenService secure,
    IUnitOfWork unitOfWork, TimeProvider clock)
{
    // Always completes quietly, whether or not the token exists.
    public async Task Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        var stored = await refreshTokens.GetByHashAsync(secure.Hash(command.RefreshToken ?? ""), cancellationToken);
        if (stored is null) return;

        stored.Revoke(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class LogoutAllHandler(
    IRefreshTokenRepository refreshTokens, ICurrentUser currentUser,
    IUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task Handle(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");
        var now = clock.GetUtcNow();

        foreach (var token in await refreshTokens.GetActiveForUserAsync(userId, cancellationToken))
            token.Revoke(now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}