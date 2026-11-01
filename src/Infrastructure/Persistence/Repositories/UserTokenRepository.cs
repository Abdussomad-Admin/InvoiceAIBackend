using Application.Auth;
using Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class UserTokenRepository(AppDbContext context) : IUserTokenRepository
{
    public Task<UserToken?> GetByHashAsync(
        UserTokenPurpose purpose, string tokenHash, CancellationToken cancellationToken = default)
        => context.UserTokens.FirstOrDefaultAsync(
            t => t.Purpose == purpose && t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<UserToken>> GetUnusedAsync(
        Guid userId, UserTokenPurpose purpose, CancellationToken cancellationToken = default)
        => await context.UserTokens
            .Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null)
            .ToListAsync(cancellationToken);

    public void Add(UserToken token) => context.UserTokens.Add(token);
}