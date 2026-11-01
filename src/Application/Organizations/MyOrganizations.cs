using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Domain.Organizations;

namespace Application.Organizations;

public sealed record MyOrganizationDto(Guid OrganizationId, string Name, OrganizationRole Role);

public sealed class GetMyOrganizationsHandler(IMembershipRepository memberships, ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<MyOrganizationDto>> Handle(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");
        return await memberships.GetOrganizationsForUserAsync(userId, cancellationToken);
    }
}