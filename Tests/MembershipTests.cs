using Domain.Common;
using Domain.Organizations;

namespace Tests;

public class MembershipTests
{
    [Fact]
    public void Create_WithValidDetails_StartsActive()
    {
        var userId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();

        var membership = Membership.Create(userId, organizationId, OrganizationRole.Accountant);

        Assert.Equal(userId, membership.UserId);
        Assert.Equal(organizationId, membership.OrganizationId);
        Assert.Equal(OrganizationRole.Accountant, membership.Role);
        Assert.True(membership.IsActive);
    }

    [Fact]
    public void Create_WithEmptyUserId_Throws()
    {
        Assert.Throws<DomainException>(
            () => Membership.Create(Guid.Empty, Guid.NewGuid(), OrganizationRole.Staff));
    }

    [Fact]
    public void Create_WithEmptyOrganizationId_Throws()
    {
        Assert.Throws<DomainException>(
            () => Membership.Create(Guid.NewGuid(), Guid.Empty, OrganizationRole.Staff));
    }

    [Fact]
    public void ChangeRole_WithUndefinedRole_Throws()
    {
        var membership = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.Staff);

        Assert.Throws<DomainException>(
            () => membership.ChangeRole((OrganizationRole)99));
    }

    [Fact]
    public void Deactivate_ThenActivate_RestoresActiveState()
    {
        // Arrange 
        var membership = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.Staff);

        // Act & Assert
        membership.Deactivate();
        Assert.False(membership.IsActive);

        membership.Activate();
        Assert.True(membership.IsActive);
    }
}