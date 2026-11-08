using Application.Assistant;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Host.Controllers;

[ApiController]
[Route("api/assistant")]
public class AssistantController(AskAssistantHandler ask, ListAssistantToolsHandler tools) : ControllerBase
{
    [HttpPost("ask")]
    [EnableRateLimiting("assistant")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<AssistantAnswerDto>> Ask(AskAssistantCommand command, CancellationToken ct)
        => Ok(await ask.Handle(command, ct));

    [HttpGet("tools")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<IReadOnlyList<AssistantToolInfoDto>>> Tools(CancellationToken ct)
        => Ok(await tools.Handle(ct));
}