namespace Domain.Organizations;

public enum Permission
{
    ViewData,
    ManageCustomers,
    ManageInvoices,
    RecordPayments,
    ViewReports,
    ManageMembers,
    ManageOrganization
}

public static class RolePermissions
{
    private static readonly Dictionary<OrganizationRole, HashSet<Permission>> Map = BuildMap();

    public static bool Has(OrganizationRole role, Permission permission)
        => Map.TryGetValue(role, out var permissions) && permissions.Contains(permission);

    private static Dictionary<OrganizationRole, HashSet<Permission>> BuildMap()
    {
        var all = Enum.GetValues<Permission>().ToHashSet();
        var staff = new HashSet<Permission>
        {
            Permission.ViewData, Permission.ManageCustomers, Permission.ManageInvoices
        };
        var accountant = new HashSet<Permission>(staff)
        {
            Permission.RecordPayments, Permission.ViewReports
        };

        return new Dictionary<OrganizationRole, HashSet<Permission>>
        {
            [OrganizationRole.Staff] = staff,
            [OrganizationRole.Accountant] = accountant,
            [OrganizationRole.Admin] = [.. all],
            [OrganizationRole.Owner] = [.. all]
        };
    }
}