using Application.Organizations;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Host.Controllers;

[ApiController]
[Route("api/members")]
public class MembersController(
    ListMembersHandler list,
    ChangeMemberRoleHandler changeRole,
    SetMemberActiveHandler setActive) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<IReadOnlyList<MemberDto>>> List(CancellationToken ct)
        => Ok(await list.Handle(ct));

    [HttpPut("{userId:guid}/role")]
    [Authorize(Policy = nameof(Permission.ManageMembers))]
    public async Task<IActionResult> ChangeRole(Guid userId, ChangeMemberRoleCommand command, CancellationToken ct)
    {
        await changeRole.Handle(userId, command, ct);
        return NoContent();
    }

    [HttpPost("{userId:guid}/deactivate")]
    [Authorize(Policy = nameof(Permission.ManageMembers))]
    public async Task<IActionResult> Deactivate(Guid userId, CancellationToken ct)
    {
        await setActive.Handle(userId, false, ct);
        return NoContent();
    }

    [HttpPost("{userId:guid}/activate")]
    [Authorize(Policy = nameof(Permission.ManageMembers))]
    public async Task<IActionResult> Activate(Guid userId, CancellationToken ct)
    {
        await setActive.Handle(userId, true, ct);
        return NoContent();
    }
}