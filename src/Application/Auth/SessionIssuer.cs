using Application.Common.Interfaces;
using Domain.Users;

namespace Application.Auth;

public sealed class SessionIssuer(
    IJwtTokenService jwt, ISecureTokenService secure,
    IRefreshTokenRepository refreshTokens, TimeProvider clock)
{
    public LoginResult Issue(User user, Guid? organizationId)
    {
        var now = clock.GetUtcNow();
        var access = jwt.Create(user.Id, user.Email, organizationId);

        var rawRefresh = secure.Generate();
        refreshTokens.Add(RefreshToken.Create(
            user.Id, organizationId, secure.Hash(rawRefresh), now.Add(jwt.RefreshLifetime)));

        return new LoginResult(access.Token, access.ExpiresAt, organizationId, rawRefresh);
    }
}