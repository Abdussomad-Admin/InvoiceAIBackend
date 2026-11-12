using Application.Common;
using Application.Invoices;
using Domain.Invoices;
using Domain.Organizations;
using Host.ImportExport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Host.Controllers;

[ApiController]
[Route("api/invoices")]
public class InvoicesController(
    CreateInvoiceHandler create,
    UpdateInvoiceHandler update,
    IssueInvoiceHandler issue,
    VoidInvoiceHandler voidInvoice,
    SendInvoiceHandler send,
    GetInvoiceHandler get,
    GetInvoicePdfHandler pdf,
    GetCreditNotePdfHandler creditNotePdf,
    ListInvoicesHandler list,
    CreditNoteHandlers creditNotes,
    ExportInvoicesHandler export) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<PagedResult<InvoiceSummaryDto>>> List(
        [FromQuery] string? search, [FromQuery] InvoiceStatus? status, [FromQuery] Guid? customerId,
        [FromQuery] bool? overdue, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await list.Handle(new ListInvoicesQuery(search, status, customerId, overdue, page, pageSize), ct));

    [HttpGet("export")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<IActionResult> Export([FromQuery] string format = "xlsx", CancellationToken ct = default)
    {
        var invoices = await export.Handle(ct);
        if (invoices.Count > 50_000)
        {
            Response.Headers["X-Export-Truncated"] = "true";
            invoices = invoices.Take(50_000).ToList();
        }
        var headers = new[] { "InvoiceNumber", "CustomerName", "CurrencyCode", "Status", "IssueDate",
            "DueDate", "Total", "AmountPaid", "Balance", "CreatedAt" };
        var rows = invoices.Select(i => new[] { i.Number ?? "", i.CustomerName, i.CurrencyCode, i.Status.ToString(),
            i.IssueDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "",
            i.DueDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "",
            i.Total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            i.AmountPaid.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            i.Balance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            i.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture) }).ToList();
        var date = DateTime.UtcNow.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        return format.Trim().ToLowerInvariant() switch
        {
            "csv" => File(SpreadsheetFiles.WriteCsv(headers, rows), "text/csv; charset=utf-8", $"invoices-{date}.csv"),
            "xlsx" => File(SpreadsheetFiles.WriteExcel("Invoices", headers, rows),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"invoices-{date}.xlsx"),
            _ => BadRequest("Format must be csv or xlsx.")
        };
    }

    [HttpGet("{id:guid}", Name = "GetInvoice")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<InvoiceDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await get.Handle(id, ct));

    [HttpGet("{id:guid}/pdf")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var file = await pdf.Handle(id, ct);
        return File(file.Content, "application/pdf", file.FileName);
    }

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

    [HttpPost("{id:guid}/send")]
    [EnableRateLimiting("auth")]
    [Authorize(Policy = nameof(Permission.IssueInvoices))]
    public async Task<ActionResult<SendInvoiceResult>> Send(
        Guid id, [FromBody] SendInvoiceCommand? command, CancellationToken ct)
        => Ok(await send.Handle(id, command ?? new SendInvoiceCommand(null), ct));

    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = nameof(Permission.IssueInvoices))]
    public async Task<ActionResult<InvoiceDto>> Void(Guid id, VoidInvoiceCommand command, CancellationToken ct)
        => Ok(await voidInvoice.Handle(id, command, ct));

    [HttpGet("{id:guid}/credit-notes")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<IReadOnlyList<CreditNoteDto>>> CreditNotes(Guid id, CancellationToken ct)
        => Ok(await creditNotes.List(id, ct));

    [HttpPost("{id:guid}/credit-notes")]
    [Authorize(Policy = nameof(Permission.IssueInvoices))]
    public async Task<ActionResult<CreditNoteDto>> CreateCreditNote(Guid id, CreateCreditNoteCommand command, CancellationToken ct)
        => Ok(await creditNotes.Create(id, command, ct));

    [HttpGet("{id:guid}/credit-notes/{creditNoteId:guid}/pdf")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<IActionResult> CreditNotePdf(Guid id, Guid creditNoteId, CancellationToken ct)
    {
        var note = await creditNotes.List(id, ct);
        if (note.All(x => x.Id != creditNoteId)) return NotFound();
        var file = await creditNotePdf.Handle(creditNoteId, ct);
        return File(file.Content, "application/pdf", file.FileName);
    }
}
