using Domain.Organizations;

namespace Application.Organizations;

public interface IMembershipRepository
{
    Task<Membership?> GetAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<Membership?> GetAnyTenantAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Membership>> GetActiveForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MyOrganizationDto>> GetOrganizationsForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemberDto>> ListMembersAsync(CancellationToken cancellationToken = default);
    Task<int> CountActiveOwnersAsync(Guid organizationId, CancellationToken cancellationToken = default);
    void Add(Membership membership);
}