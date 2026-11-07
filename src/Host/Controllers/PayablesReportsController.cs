using Application.Reports;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Host.Controllers;

[ApiController]
public class PayablesReportsController(
    GetSupplierSummaryHandler supplierSummary,
    GetPayablesAgingHandler aging,
    GetExpenseReportHandler expenses) : ControllerBase
{
    [HttpGet("api/suppliers/{id:guid}/summary")]
    [Authorize(Policy = nameof(Permission.ViewPayables))]
    public async Task<ActionResult<SupplierSummaryDto>> SupplierSummary(Guid id, CancellationToken ct)
        => Ok(await supplierSummary.Handle(id, ct));

    [HttpGet("api/reports/payables-aging")]
    [Authorize(Policy = nameof(Permission.ViewReports))]
    public async Task<ActionResult<PayablesAgingReportDto>> Aging(CancellationToken ct)
        => Ok(await aging.Handle(ct));

    [HttpGet("api/reports/expenses")]
    [Authorize(Policy = nameof(Permission.ViewReports))]
    public async Task<ActionResult<ExpenseReportDto>> Expenses(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Ok(await expenses.Handle(new ExpenseQuery(from, to), ct));
}