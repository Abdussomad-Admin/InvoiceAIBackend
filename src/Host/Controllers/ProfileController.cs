using Application.Auth;
using Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Host.Controllers;

[ApiController]
[Authorize]
[Route("api/profile")]
public class ProfileController(
    GetProfileHandler get,
    UpdateProfileHandler update,
    ChangePasswordHandler changePassword) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ProfileDto>> Get(CancellationToken ct)
        => Ok(await get.Handle(ct));

    [HttpPut]
    public async Task<ActionResult<ProfileDto>> Update(UpdateProfileCommand command, CancellationToken ct)
        => Ok(await update.Handle(command, ct));

    [HttpPost("change-password")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResult>> ChangePassword(ChangePasswordCommand command, CancellationToken ct)
        => Ok(await changePassword.Handle(command, ct));
}