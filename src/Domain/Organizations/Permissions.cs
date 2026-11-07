namespace Domain.Organizations;

public enum Permission
{
    ViewData,
    ManageCustomers,
    ManageInvoices,
    IssueInvoices,
    RecordPayments,
    ViewReports,
    ManageMembers,
    ManageOrganization, 
    ViewAuditLog,
    ViewPayables,
    ManagePayables
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
            Permission.IssueInvoices, Permission.RecordPayments, Permission.ViewReports,
            Permission.ViewPayables, Permission.ManagePayables
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