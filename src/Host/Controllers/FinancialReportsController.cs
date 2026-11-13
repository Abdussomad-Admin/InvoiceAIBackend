using Application.Reports;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Host.Controllers;

[ApiController]
public class FinancialReportsController(
    GetFinancialSummaryHandler summary,
    GetYearComparisonHandler comparison,
    GetCashFlowForecastHandler forecast,
    GetFinancialRecordsReportHandler records,
    DownloadFinancialRecordsReportHandler recordsPdf) : ControllerBase
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

    [HttpGet("api/reports/cash-flow-forecast")]
    [Authorize(Policy = nameof(Permission.ViewReports))]
    public async Task<ActionResult<CashFlowForecastDto>> CashFlowForecast(CancellationToken ct)
        => Ok(await forecast.Handle(ct));

    [HttpGet("api/reports/financial-records")]
    [Authorize(Policy = nameof(Permission.ViewReports))]
    public async Task<ActionResult<FinancialRecordsReportDto>> FinancialRecords(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Ok(await records.Handle(new FinancialRecordsReportQuery(from, to), ct));

    [HttpGet("api/reports/financial-records/pdf")]
    [Authorize(Policy = nameof(Permission.ViewReports))]
    public async Task<IActionResult> FinancialRecordsPdf(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var file = await recordsPdf.Handle(new FinancialRecordsReportQuery(from, to), ct);
        return File(file.Content, "application/pdf", file.FileName);
    }
}

