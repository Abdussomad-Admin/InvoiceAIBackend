using Application.Common.Interfaces;

namespace Host;

public class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId
        => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value, out var id) ? id : null;
}