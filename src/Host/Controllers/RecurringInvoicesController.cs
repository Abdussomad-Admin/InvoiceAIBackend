using Application.Invoices;
using Domain.Invoices;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Host.Controllers;

[ApiController]
[Route("api/recurring-invoices")]
[Authorize]
public sealed class RecurringInvoicesController(RecurringInvoiceHandlers handlers) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<IReadOnlyList<RecurringInvoiceDto>>> List(CancellationToken ct)
        => Ok(await handlers.List(ct));

    [HttpPost]
    [Authorize(Policy = nameof(Permission.ManageInvoices))]
    public async Task<ActionResult<RecurringInvoiceDto>> Create(CreateRecurringInvoiceCommand command, CancellationToken ct)
        => Ok(await handlers.Create(command, ct));

    [HttpPost("{id:guid}/pause")]
    [Authorize(Policy = nameof(Permission.ManageInvoices))]
    public async Task<ActionResult<RecurringInvoiceDto>> Pause(Guid id, CancellationToken ct)
        => Ok(await handlers.SetActive(id, false, ct));

    [HttpPost("{id:guid}/resume")]
    [Authorize(Policy = nameof(Permission.ManageInvoices))]
    public async Task<ActionResult<RecurringInvoiceDto>> Resume(Guid id, CancellationToken ct)
        => Ok(await handlers.SetActive(id, true, ct));
}
