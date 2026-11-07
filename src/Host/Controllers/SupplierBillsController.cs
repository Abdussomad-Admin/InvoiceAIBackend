using Application.Bills;
using Application.Common;
using Domain.Bills;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Host.Controllers;

[ApiController]
[Route("api/supplier-bills")]
public class SupplierBillsController(
    CreateBillHandler create,
    UpdateBillHandler update,
    FinalizeBillHandler finalize,
    VoidBillHandler voidBill,
    GetBillHandler get,
    ListBillsHandler list) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = nameof(Permission.ViewPayables))]
    public async Task<ActionResult<PagedResult<BillSummaryDto>>> List(
        [FromQuery] string? search, [FromQuery] BillStatus? status, [FromQuery] Guid? supplierId,
        [FromQuery] bool? overdue, [FromQuery] DateOnly? dueBy,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await list.Handle(new ListBillsQuery(search, status, supplierId, overdue, dueBy, page, pageSize), ct));

    [HttpGet("{id:guid}", Name = "GetBill")]
    [Authorize(Policy = nameof(Permission.ViewPayables))]
    public async Task<ActionResult<BillDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await get.Handle(id, ct));

    [HttpPost]
    [Authorize(Policy = nameof(Permission.ManagePayables))]
    public async Task<ActionResult<BillDto>> Create(BillInput input, CancellationToken ct)
    {
        var dto = await create.Handle(input, ct);
        return CreatedAtRoute("GetBill", new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = nameof(Permission.ManagePayables))]
    public async Task<ActionResult<BillDto>> Update(Guid id, BillInput input, CancellationToken ct)
        => Ok(await update.Handle(id, input, ct));

    [HttpPost("{id:guid}/finalize")]
    [Authorize(Policy = nameof(Permission.ManagePayables))]
    public async Task<ActionResult<BillDto>> Finalize(
        Guid id, [FromBody] FinalizeBillCommand? command, CancellationToken ct)
        => Ok(await finalize.Handle(id, command ?? new FinalizeBillCommand(false), ct));

    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = nameof(Permission.ManagePayables))]
    public async Task<ActionResult<BillDto>> Void(Guid id, VoidBillCommand command, CancellationToken ct)
        => Ok(await voidBill.Handle(id, command, ct));
}