using Application.Bills;
using Application.Common;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Host.Controllers;

[ApiController]
public class BillPaymentsController(
    RecordBillPaymentHandler record,
    ReverseBillPaymentHandler reverse,
    ListBillPaymentsHandler list) : ControllerBase
{
    [HttpPost("api/supplier-bills/{billId:guid}/payments")]
    [Authorize(Policy = nameof(Permission.ManagePayables))]
    public async Task<ActionResult<BillPaymentDto>> Record(
        Guid billId, RecordBillPaymentCommand command, CancellationToken ct)
        => Ok(await record.Handle(billId, command, ct));

    [HttpGet("api/supplier-bills/{billId:guid}/payments")]
    [Authorize(Policy = nameof(Permission.ViewPayables))]
    public async Task<ActionResult<PagedResult<BillPaymentDto>>> ForBill(Guid billId, CancellationToken ct)
        => Ok(await list.Handle(new ListBillPaymentsQuery(billId, null, null, null, true, 1, 100), ct));

    [HttpGet("api/supplier-payments")]
    [Authorize(Policy = nameof(Permission.ViewPayables))]
    public async Task<ActionResult<PagedResult<BillPaymentDto>>> List(
        [FromQuery] Guid? supplierId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] bool includeReversed = true, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await list.Handle(new ListBillPaymentsQuery(null, supplierId, from, to, includeReversed, page, pageSize), ct));

    [HttpPost("api/supplier-payments/{id:guid}/reverse")]
    [Authorize(Policy = nameof(Permission.ManagePayables))]
    public async Task<ActionResult<BillPaymentDto>> Reverse(Guid id, ReverseBillPaymentCommand command, CancellationToken ct)
        => Ok(await reverse.Handle(id, command, ct));
}