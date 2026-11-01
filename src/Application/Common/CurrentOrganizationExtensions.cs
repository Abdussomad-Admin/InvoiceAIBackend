using Application.Common.Exceptions;
using Application.Common.Interfaces;

namespace Application.Common;

public static class CurrentOrganizationExtensions
{
    public static Guid RequireId(this ICurrentOrganization current)
        => current.OrganizationId ?? throw new ForbiddenException("No organization is selected for this request.");
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);