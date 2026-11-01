using Application.Common.Interfaces;

namespace Host;

public class HttpContextCurrentOrganization(IHttpContextAccessor httpContextAccessor) : ICurrentOrganization
{
    public Guid? OrganizationId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirst("org_id")?.Value;
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }
}