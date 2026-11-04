using Application.Reports;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Host.Controllers;

[ApiController]
public class ReceivablesController(GetCustomerSummaryHandler customerSummary, GetAgingHandler aging) : ControllerBase
{
    [HttpGet("api/customers/{id:guid}/summary")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<CustomerSummaryDto>> CustomerSummary(Guid id, CancellationToken ct)
        => Ok(await customerSummary.Handle(id, ct));

    [HttpGet("api/reports/receivables-aging")]
    [Authorize(Policy = nameof(Permission.ViewReports))]
    public async Task<ActionResult<AgingReportDto>> Aging(CancellationToken ct)
        => Ok(await aging.Handle(ct));
}