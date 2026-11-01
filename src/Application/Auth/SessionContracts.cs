using Domain.Users;

namespace Application.Auth;

public sealed record RefreshCommand(string RefreshToken, Guid? OrganizationId = null);
public sealed record LogoutCommand(string RefreshToken);

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RefreshToken>> GetActiveForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    void Add(RefreshToken token);
}