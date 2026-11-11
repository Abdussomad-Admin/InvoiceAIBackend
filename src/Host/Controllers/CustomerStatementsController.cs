using Application.Customers;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Host.Controllers;

[ApiController]
[Route("api/customers/{customerId:guid}/statement")]
public sealed class CustomerStatementsController(
    GetCustomerStatementHandler getStatement,
    GetCustomerStatementPdfHandler getPdf,
    SendCustomerStatementHandler send) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<CustomerStatementDto>> Get(
        Guid customerId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Ok(await getStatement.Handle(customerId, from, to, ct));

    [HttpGet("pdf")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<IActionResult> Pdf(
        Guid customerId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var file = await getPdf.Handle(customerId, from, to, ct);
        return File(file.Content, "application/pdf", file.FileName);
    }

    [HttpPost("email")]
    [EnableRateLimiting("auth")]
    [Authorize(Policy = nameof(Permission.IssueInvoices))]
    public async Task<ActionResult<SendCustomerStatementResult>> Email(
        Guid customerId, SendCustomerStatementCommand command, CancellationToken ct)
        => Ok(await send.Handle(customerId, command, ct));
}
