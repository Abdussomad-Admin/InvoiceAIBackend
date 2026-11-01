using Domain.Common;

namespace Domain.Organizations;

public sealed class Membership : BaseEntity, ITenantOwned
{
    private Membership() { }

    public Guid UserId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public OrganizationRole Role { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Membership Create(Guid userId, Guid organizationId, OrganizationRole role)
    {
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required.");

        if (organizationId == Guid.Empty)
            throw new DomainException("OrganizationId is required.");

        if (!Enum.IsDefined(role))
            throw new DomainException("Role is not valid.");

        return new Membership
        {
            UserId = userId,
            OrganizationId = organizationId,
            Role = role
        };
    }

    public void ChangeRole(OrganizationRole role)
    {
        if (!Enum.IsDefined(role))
            throw new DomainException("Role is not valid.");

        Role = role;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}