using Application.Audit;
using Application.Common;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Host.Controllers;

[ApiController]
[Route("api/audit")]
public class AuditController(ListAuditHandler list) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = nameof(Permission.ViewAuditLog))]
    public async Task<ActionResult<PagedResult<AuditEntryDto>>> List(
        [FromQuery] string? entityType, [FromQuery] Guid? entityId, [FromQuery] Guid? userId,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => Ok(await list.Handle(new ListAuditQuery(entityType, entityId, userId, from, to, page, pageSize), ct));
}