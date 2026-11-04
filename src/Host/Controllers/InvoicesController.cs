using Application.Common;
using Application.Invoices;
using Domain.Invoices;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Host.Controllers;

[ApiController]
[Route("api/invoices")]
public class InvoicesController(
    CreateInvoiceHandler create,
    UpdateInvoiceHandler update,
    IssueInvoiceHandler issue,
    VoidInvoiceHandler voidInvoice,
    GetInvoiceHandler get,
    ListInvoicesHandler list) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<PagedResult<InvoiceSummaryDto>>> List(
        [FromQuery] string? search, [FromQuery] InvoiceStatus? status, [FromQuery] Guid? customerId,
        [FromQuery] bool? overdue, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await list.Handle(new ListInvoicesQuery(search, status, customerId, overdue, page, pageSize), ct));

    [HttpGet("{id:guid}", Name = "GetInvoice")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<InvoiceDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await get.Handle(id, ct));

    [HttpPost]
    [Authorize(Policy = nameof(Permission.ManageInvoices))]
    public async Task<ActionResult<InvoiceDto>> Create(InvoiceInput input, CancellationToken ct)
    {
        var dto = await create.Handle(input, ct);
        return CreatedAtRoute("GetInvoice", new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = nameof(Permission.ManageInvoices))]
    public async Task<ActionResult<InvoiceDto>> Update(Guid id, InvoiceInput input, CancellationToken ct)
        => Ok(await update.Handle(id, input, ct));

    [HttpPost("{id:guid}/issue")]
    [Authorize(Policy = nameof(Permission.IssueInvoices))]
    public async Task<ActionResult<InvoiceDto>> Issue(
        Guid id, [FromBody] IssueInvoiceCommand? command, CancellationToken ct)
        => Ok(await issue.Handle(id, command?.DueDate, ct));

    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = nameof(Permission.IssueInvoices))]
    public async Task<ActionResult<InvoiceDto>> Void(Guid id, VoidInvoiceCommand command, CancellationToken ct)
        => Ok(await voidInvoice.Handle(id, command, ct));
}