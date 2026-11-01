using Application.Organizations;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;

namespace Host.Authorization
{
    public sealed class PermissionRequirement(Permission permission) : IAuthorizationRequirement
    {
        public Permission Permission { get; } = permission;
    }

    public sealed class PermissionAuthorizationHandler(IMembershipRepository memberships)
        : AuthorizationHandler<PermissionRequirement>
    {
        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            var userClaim = context.User.FindFirst("sub")?.Value;
            var organizationClaim = context.User.FindFirst("org_id")?.Value;

            if (!Guid.TryParse(userClaim, out var userId) ||
                !Guid.TryParse(organizationClaim, out var organizationId))
                return;

            var membership = await memberships.GetAsync(userId, organizationId);
            if (membership is null || !membership.IsActive) return;

            if (RolePermissions.Has(membership.Role, requirement.Permission))
                context.Succeed(requirement);
        }
    }
}