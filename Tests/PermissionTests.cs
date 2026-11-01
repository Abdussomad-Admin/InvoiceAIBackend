using Domain.Organizations;

namespace Tests;

public class PermissionTests
{
    [Fact]
    public void Owner_HasEveryPermission()
    {
        foreach (var permission in Enum.GetValues<Permission>())
            Assert.True(RolePermissions.Has(OrganizationRole.Owner, permission));
    }

    [Fact]
    public void Staff_CanManageCustomers_ButNotRecordPaymentsOrManageMembers()
    {
        Assert.True(RolePermissions.Has(OrganizationRole.Staff, Permission.ManageCustomers));
        Assert.False(RolePermissions.Has(OrganizationRole.Staff, Permission.RecordPayments));
        Assert.False(RolePermissions.Has(OrganizationRole.Staff, Permission.ManageMembers));
    }

    [Fact]
    public void Accountant_CanRecordPayments_ButNotManageMembers()
    {
        Assert.True(RolePermissions.Has(OrganizationRole.Accountant, Permission.RecordPayments));
        Assert.False(RolePermissions.Has(OrganizationRole.Accountant, Permission.ManageMembers));
    }

    [Fact]
    public void Admin_CanManageMembersAndOrganization()
    {
        Assert.True(RolePermissions.Has(OrganizationRole.Admin, Permission.ManageMembers));
        Assert.True(RolePermissions.Has(OrganizationRole.Admin, Permission.ManageOrganization));
    }
}