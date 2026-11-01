using Application.Organizations;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Host.Controllers;

[ApiController]
[Route("api/invitations")]
public class InvitationsController(
    InviteMemberHandler invite,
    AcceptInvitationHandler accept,
    ListInvitationsHandler list,
    RevokeInvitationHandler revoke) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = nameof(Permission.ManageMembers))]
    public async Task<ActionResult<IReadOnlyList<InvitationDto>>> List(CancellationToken ct)
        => Ok(await list.Handle(ct));

    [HttpPost]
    [EnableRateLimiting("auth")]
    [Authorize(Policy = nameof(Permission.ManageMembers))]
    public async Task<ActionResult<InvitationDto>> Invite(InviteMemberCommand command, CancellationToken ct)
        => Ok(await invite.Handle(command, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = nameof(Permission.ManageMembers))]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await revoke.Handle(id, ct);
        return NoContent();
    }

    // No sign-in needed: the emailed single-use token is the proof.
    [HttpPost("accept")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AcceptInvitationResult>> Accept(AcceptInvitationCommand command, CancellationToken ct)
        => Ok(await accept.Handle(command, ct));
}