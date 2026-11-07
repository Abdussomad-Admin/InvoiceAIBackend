using Application.Common;
using Application.Suppliers;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Host.Controllers;

[ApiController]
[Route("api/suppliers")]
public class SuppliersController(
    CreateSupplierHandler create,
    UpdateSupplierHandler update,
    ArchiveSupplierHandler archive,
    GetSupplierHandler get,
    ListSuppliersHandler list) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = nameof(Permission.ViewPayables))]
    public async Task<ActionResult<PagedResult<SupplierDto>>> List(
        [FromQuery] string? search, [FromQuery] bool includeArchived = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await list.Handle(new ListSuppliersQuery(search, includeArchived, page, pageSize), ct));

    [HttpGet("{id:guid}", Name = "GetSupplier")]
    [Authorize(Policy = nameof(Permission.ViewPayables))]
    public async Task<ActionResult<SupplierDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await get.Handle(id, ct));

    [HttpPost]
    [Authorize(Policy = nameof(Permission.ManagePayables))]
    public async Task<ActionResult<SupplierDto>> Create(SupplierInput input, CancellationToken ct)
    {
        var dto = await create.Handle(input, ct);
        return CreatedAtRoute("GetSupplier", new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = nameof(Permission.ManagePayables))]
    public async Task<ActionResult<SupplierDto>> Update(Guid id, SupplierInput input, CancellationToken ct)
        => Ok(await update.Handle(id, input, ct));

    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = nameof(Permission.ManagePayables))]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await archive.Handle(id, true, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = nameof(Permission.ManagePayables))]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        await archive.Handle(id, false, ct);
        return NoContent();
    }
}