namespace Domain.Organizations;

public static class MemberRules
{
    // Which roles may the actor hand out? Owners: any. Admins: anything except Owner.
    public static bool CanAssign(OrganizationRole actor, OrganizationRole target)
        => actor == OrganizationRole.Owner
           || (actor == OrganizationRole.Admin && target != OrganizationRole.Owner);

    // Whose membership may the actor change? Owners: anyone. Admins: anyone except Owners.
    public static bool CanManage(OrganizationRole actor, OrganizationRole targetCurrentRole)
        => actor == OrganizationRole.Owner
           || (actor == OrganizationRole.Admin && targetCurrentRole != OrganizationRole.Owner);
}