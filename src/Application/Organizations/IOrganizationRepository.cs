using Domain.Organizations;

namespace Application.Organizations
{
    public interface IOrganizationRepository
    {
        Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        void Add(Organization organization);
    }
}