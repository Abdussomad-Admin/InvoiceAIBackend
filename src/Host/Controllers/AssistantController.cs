using Application.Assistant;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Host.Controllers;

[ApiController]
[Route("api/assistant")]
public class AssistantController(
    StartAssistantSessionHandler startSession, ExecuteAssistantToolHandler executeTool,
    ListAssistantToolsHandler tools) : ControllerBase
{
    [HttpPost("ask")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult Ask() => StatusCode(StatusCodes.Status410Gone,
        new { error = "The assistant now uses Firebase AI Logic from the Invoice AI web app. Update the frontend to the latest version." });

    [HttpPost("session")]
    [EnableRateLimiting("assistant")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<AssistantSessionDto>> StartSession(
        StartAssistantSessionCommand command, CancellationToken ct)
        => Ok(await startSession.Handle(command, ct));

    [HttpGet("tools")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<IReadOnlyList<AssistantToolInfoDto>>> Tools(CancellationToken ct)
        => Ok(await tools.Handle(ct));

    [HttpPost("tools/{toolName}")]
    [EnableRateLimiting("assistant-tool")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<AssistantToolResultDto>> ExecuteTool(
        string toolName, ExecuteAssistantToolCommand command, CancellationToken ct)
        => Ok(await executeTool.Handle(toolName, command, ct));
}
