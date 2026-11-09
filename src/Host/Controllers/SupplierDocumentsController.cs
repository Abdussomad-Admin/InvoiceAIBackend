using Application.Common;
using Application.Documents;
using Domain.Common;
using Domain.Documents;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Host.Controllers;

[ApiController]
[Route("api/supplier-documents")]
public class SupplierDocumentsController(
    UploadDocumentHandler upload,
    ListDocumentsHandler list,
    GetDocumentHandler get,
    DownloadDocumentHandler download,
    RejectDocumentHandler reject,
    ExtractDocumentHandler extract,
    GetExtractionHandler getExtraction,
    ListExtractionsHandler extractionHistory) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = nameof(Permission.ViewPayables))]
    public async Task<ActionResult<PagedResult<DocumentSummaryDto>>> List(
        [FromQuery] DocumentStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await list.Handle(new ListDocumentsQuery(status, page, pageSize), ct));

    [HttpGet("{id:guid}", Name = "GetDocument")]
    [Authorize(Policy = nameof(Permission.ViewPayables))]
    public async Task<ActionResult<DocumentDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await get.Handle(id, ct));

    [HttpGet("{id:guid}/download")]
    [Authorize(Policy = nameof(Permission.ViewPayables))]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var file = await download.Handle(id, ct);

        // Served as an attachment with a type WE detected, so an uploaded file can never run as a web page.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost]
    [EnableRateLimiting("upload")]
    [RequestSizeLimit(DocumentRules.MaxBytes + 512 * 1024)]
    [Consumes("multipart/form-data")]
    [Authorize(Policy = nameof(Permission.ManagePayables))]
    public async Task<ActionResult<DocumentDto>> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new DomainException("Choose a file to upload.");
        if (file.Length > DocumentRules.MaxBytes)
            throw new DomainException($"The file is larger than {DocumentRules.MaxBytes / (1024 * 1024)} MB.");

        var content = new byte[file.Length];
        await using (var stream = file.OpenReadStream())
            await stream.ReadExactlyAsync(content, ct);

        var dto = await upload.Handle(new UploadDocumentCommand(file.FileName, content), ct);
        return CreatedAtRoute("GetDocument", new { id = dto.Id }, dto);
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = nameof(Permission.ManagePayables))]
    public async Task<ActionResult<DocumentDto>> Reject(Guid id, RejectDocumentCommand command, CancellationToken ct)
        => Ok(await reject.Handle(id, command, ct));

    // Sends the document's text or image to the configured AI model. Only happens when someone asks.
    [HttpPost("{id:guid}/extract")]
    [EnableRateLimiting("extract")]
    [Authorize(Policy = nameof(Permission.ManagePayables))]
    public async Task<ActionResult<ExtractionReviewDto>> Extract(Guid id, CancellationToken ct)
        => Ok(await extract.Handle(id, ct));

    [HttpGet("{id:guid}/extraction")]
    [Authorize(Policy = nameof(Permission.ViewPayables))]
    public async Task<ActionResult<ExtractionReviewDto>> GetExtraction(Guid id, CancellationToken ct)
        => Ok(await getExtraction.Handle(id, ct));

    [HttpGet("{id:guid}/extractions")]
    [Authorize(Policy = nameof(Permission.ViewPayables))]
    public async Task<ActionResult<IReadOnlyList<ExtractionAttemptDto>>> ExtractionHistory(Guid id, CancellationToken ct)
        => Ok(await extractionHistory.Handle(id, ct));
}