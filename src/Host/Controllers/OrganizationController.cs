using Application.Organizations;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Host.Controllers;

[ApiController]
[Route("api/organization")]
public class OrganizationController(
    GetOrganizationHandler get,
    UpdateOrganizationHandler update) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<OrganizationDto>> Get(CancellationToken ct)
        => Ok(await get.Handle(ct));

    [HttpPut]
    [Authorize(Policy = nameof(Permission.ManageOrganization))]
    public async Task<ActionResult<OrganizationDto>> Update(UpdateOrganizationInput input, CancellationToken ct)
        => Ok(await update.Handle(input, ct));
}