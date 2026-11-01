using Application.Organizations;
using Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class OrganizationRepository(AppDbContext context) : IOrganizationRepository
{
    public Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => context.Organizations.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public void Add(Organization organization) => context.Organizations.Add(organization);
}