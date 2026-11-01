using Application.Organizations;
using Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class MembershipRepository(AppDbContext context) : IMembershipRepository
{
    public Task<Membership?> GetAsync(
        Guid userId, Guid organizationId, CancellationToken cancellationToken = default)
        => context.Memberships.FirstOrDefaultAsync(
            m => m.UserId == userId && m.OrganizationId == organizationId, cancellationToken);

    public Task<Membership?> GetAnyTenantAsync(
        Guid userId, Guid organizationId, CancellationToken cancellationToken = default)
        // Deliberate exemption from the tenant filter: used when accepting an invitation.
        => context.Memberships.IgnoreQueryFilters().FirstOrDefaultAsync(
            m => m.UserId == userId && m.OrganizationId == organizationId, cancellationToken);

    public async Task<IReadOnlyList<Membership>> GetActiveForUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
        // Deliberate exemption from the tenant filter: used right after login, before an organization
        // is chosen. It is still restricted to this user's own rows.
        => await context.Memberships
            .IgnoreQueryFilters()
            .Where(m => m.UserId == userId && m.IsActive)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MyOrganizationDto>> GetOrganizationsForUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
        // Deliberate exemption from the tenant filter: the user's own organizations, across tenants.
        => await (
            from m in context.Memberships.IgnoreQueryFilters().AsNoTracking()
            join o in context.Organizations.AsNoTracking() on m.OrganizationId equals o.Id
            where m.UserId == userId && m.IsActive && !o.IsArchived
            orderby o.Name
            select new MyOrganizationDto(o.Id, o.Name, m.Role))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MemberDto>> ListMembersAsync(CancellationToken cancellationToken = default)
        => await (
            from m in context.Memberships.AsNoTracking()
            join u in context.Users.AsNoTracking() on m.UserId equals u.Id
            orderby u.FullName
            select new MemberDto(u.Id, u.Email, u.FullName, m.Role, m.IsActive, m.CreatedAt))
            .ToListAsync(cancellationToken);

    public Task<int> CountActiveOwnersAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => context.Memberships.CountAsync(
            m => m.OrganizationId == organizationId
              && m.IsActive
              && m.Role == OrganizationRole.Owner,
            cancellationToken);

    public void Add(Membership membership) => context.Memberships.Add(membership);
}