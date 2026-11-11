using System.Security.Cryptography;
using Application.Banking;
using Domain.Organizations;
using Host.ImportExport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Host.Controllers;

[ApiController]
[Route("api/bank-statements")]
public sealed class BankStatementsController(
    ImportBankStatementHandler import,
    GetBankStatementHandler get,
    GetBankStatementSuggestionsHandler suggestions,
    MatchBankStatementPaymentHandler match,
    IgnoreBankStatementLineHandler ignore) : ControllerBase
{
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting("upload")]
    [RequestSizeLimit(SpreadsheetFiles.MaxImportBytes)]
    [Authorize(Policy = nameof(Permission.RecordPayments))]
    public async Task<ActionResult<BankStatementImportResult>> Import(
        [FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0) return BadRequest("Choose a non-empty .csv or .xlsx file.");
        if (file.Length > SpreadsheetFiles.MaxImportBytes) return BadRequest("The file must be 5 MB or smaller.");

        byte[] bytes;
        await using (var stream = new MemoryStream())
        {
            await file.CopyToAsync(stream, cancellationToken);
            bytes = stream.ToArray();
        }

        SpreadsheetData data;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            data = SpreadsheetFiles.Read(stream, file.FileName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            return BadRequest(ex.Message);
        }

        var headers = data.Headers.Select(h => h.Trim()).ToArray();
        if (headers.Any(string.IsNullOrWhiteSpace)) return BadRequest("Every column must have a header.");
        if (headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            return BadRequest("Column headers must be unique.");
        var required = new[] { "TransactionDate", "Description", "Amount", "CurrencyCode" };
        var missing = required.Where(name => !headers.Contains(name, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (missing.Length > 0) return BadRequest($"Missing required columns: {string.Join(", ", missing)}.");

        int? Column(string name)
        {
            var index = Array.FindIndex(headers, h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? null : index;
        }
        string? Value(string[] row, string name)
        {
            var index = Column(name);
            return index is { } i && i < row.Length && !string.IsNullOrWhiteSpace(row[i]) ? row[i].Trim() : null;
        }

        var rows = data.Rows.Select((row, index) => new BankStatementImportRow(index + 2,
            Value(row, "TransactionDate"), Value(row, "Description"), Value(row, "Amount"),
            Value(row, "CurrencyCode"), Value(row, "Reference"))).ToList();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        return Ok(await import.Handle(file.FileName, hash, rows, cancellationToken));
    }

    [HttpGet("{batchId:guid}")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<BankStatementDetailsDto>> Get(Guid batchId, CancellationToken ct)
        => Ok(await get.Handle(batchId, ct));

    [HttpGet("lines/{lineId:guid}/suggestions")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<BankStatementSuggestionsDto>> Suggestions(Guid lineId, CancellationToken ct)
        => Ok(await suggestions.Handle(lineId, ct));

    [HttpPost("lines/{lineId:guid}/match")]
    [Authorize(Policy = nameof(Permission.RecordPayments))]
    public async Task<ActionResult<BankStatementLineDto>> Match(
        Guid lineId, MatchStatementPaymentCommand command, CancellationToken ct)
        => Ok(await match.Handle(lineId, command, ct));

    [HttpPost("lines/{lineId:guid}/ignore")]
    [Authorize(Policy = nameof(Permission.RecordPayments))]
    public async Task<ActionResult<BankStatementLineDto>> Ignore(
        Guid lineId, IgnoreStatementLineCommand command, CancellationToken ct)
        => Ok(await ignore.Handle(lineId, command, ct));
}
