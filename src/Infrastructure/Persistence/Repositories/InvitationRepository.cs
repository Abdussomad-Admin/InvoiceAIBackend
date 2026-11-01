using Application.Organizations;
using Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class InvitationRepository(AppDbContext context) : IInvitationRepository
{
    public Task<Invitation?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        // Deliberate exemption from the tenant filter: the person accepting is not yet in the organization.
        => context.Invitations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);

    public Task<Invitation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => context.Invitations.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Invitation>> GetPendingByEmailAsync(
        string email, DateTimeOffset now, CancellationToken cancellationToken = default)
        => await context.Invitations
            .Where(i => i.Email == email && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Invitation>> ListPendingAsync(
        DateTimeOffset now, CancellationToken cancellationToken = default)
        => await context.Invitations.AsNoTracking()
            .Where(i => i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

    public void Add(Invitation invitation) => context.Invitations.Add(invitation);
}