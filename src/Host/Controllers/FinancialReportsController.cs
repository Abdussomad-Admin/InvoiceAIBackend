using Application.Reports;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Host.Controllers;

[ApiController]
public class FinancialReportsController(
    GetFinancialSummaryHandler summary,
    GetYearComparisonHandler comparison) : ControllerBase
{
    [HttpGet("api/reports/financial-summary")]
    [Authorize(Policy = nameof(Permission.ViewReports))]
    public async Task<ActionResult<FinancialSummaryDto>> Summary(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Ok(await summary.Handle(new FinancialSummaryQuery(from, to), ct));

    [HttpGet("api/reports/year-comparison")]
    [Authorize(Policy = nameof(Permission.ViewReports))]
    public async Task<ActionResult<YearComparisonDto>> YearComparison(
        [FromQuery] int? year, CancellationToken ct)
        => Ok(await comparison.Handle(new YearComparisonQuery(year), ct));
}

