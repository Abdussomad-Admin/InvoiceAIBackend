using Application.Common;
using Application.Customers;
using Domain.Organizations;
using Host.ImportExport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Host.Controllers
{
    [ApiController]
    [Route("api/customers")]
    public class CustomersController(
        CreateCustomerHandler create,
        UpdateCustomerHandler update,
        ArchiveCustomerHandler archive,
        GetCustomerHandler get,
        ListCustomersHandler list,
        ImportCustomersHandler import,
        ExportCustomersHandler export) : ControllerBase
    {
        [HttpGet]
        [Authorize(Policy = nameof(Permission.ViewData))]
        public async Task<ActionResult<PagedResult<CustomerDto>>> List(
            [FromQuery] string? search, [FromQuery] bool includeArchived = false,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
            => Ok(await list.Handle(new ListCustomersQuery(search, includeArchived, page, pageSize), ct));

        [HttpGet("{id:guid}", Name = "GetCustomer")]
        [Authorize(Policy = nameof(Permission.ViewData))]
        public async Task<ActionResult<CustomerDto>> GetById(Guid id, CancellationToken ct)
            => Ok(await get.Handle(id, ct));

        [HttpPost]
        [Authorize(Policy = nameof(Permission.ManageCustomers))]
        public async Task<ActionResult<CustomerDto>> Create(CustomerInput input, CancellationToken ct)
        {
            var dto = await create.Handle(input, ct);
            return CreatedAtRoute("GetCustomer", new { id = dto.Id }, dto);
        }

        [HttpPut("{id:guid}")]
        [Authorize(Policy = nameof(Permission.ManageCustomers))]
        public async Task<ActionResult<CustomerDto>> Update(Guid id, CustomerInput input, CancellationToken ct)
            => Ok(await update.Handle(id, input, ct));

        [HttpPost("{id:guid}/archive")]
        [Authorize(Policy = nameof(Permission.ManageCustomers))]
        public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
        {
            await archive.Handle(id, true, ct);
            return NoContent();
        }

        [HttpPost("{id:guid}/restore")]
        [Authorize(Policy = nameof(Permission.ManageCustomers))]
        public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
        {
            await archive.Handle(id, false, ct);
            return NoContent();
        }

        [HttpGet("export")]
        [Authorize(Policy = nameof(Permission.ViewData))]
        public async Task<IActionResult> Export([FromQuery] string format = "xlsx", CancellationToken ct = default)
        {
            var customers = await export.Handle(ct);
            if (customers.Count > 50_000)
            {
                Response.Headers["X-Export-Truncated"] = "true";
                customers = customers.Take(50_000).ToList();
            }
            var headers = new[] { "Name", "Email", "Phone", "TaxIdentificationNumber", "AddressLine1",
                "AddressLine2", "City", "Region", "PostalCode", "CountryCode", "Notes" };
            var rows = customers.Select(c => new[] { c.Name, c.Email ?? "", c.Phone ?? "",
                c.TaxIdentificationNumber ?? "", c.AddressLine1 ?? "", c.AddressLine2 ?? "", c.City ?? "",
                c.Region ?? "", c.PostalCode ?? "", c.CountryCode ?? "", c.Notes ?? "" }).ToList();
            return ExportFile("customers", "Customers", format, headers, rows);
        }

        [HttpPost("import")]
        [Consumes("multipart/form-data")]
        [EnableRateLimiting("upload")]
        [RequestSizeLimit(SpreadsheetFiles.MaxImportBytes)]
        [Authorize(Policy = nameof(Permission.ManageCustomers))]
        public async Task<IActionResult> Import([FromForm] IFormFile? file, CancellationToken ct)
        {
            if (file is null || file.Length == 0) return BadRequest("Choose a non-empty .csv or .xlsx file.");
            if (file.Length > SpreadsheetFiles.MaxImportBytes) return BadRequest("The file must be 5 MB or smaller.");

            SpreadsheetData data;
            try
            {
                await using var stream = file.OpenReadStream();
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
            if (!headers.Contains("Name", StringComparer.OrdinalIgnoreCase))
                return BadRequest("A Name column is required.");

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

            var rows = data.Rows.Select((row, index) => new CustomerImportRow(index + 2,
                new CustomerInput(Value(row, "Name") ?? "", Value(row, "Email"), Value(row, "Phone"),
                    Value(row, "TaxIdentificationNumber"), Value(row, "AddressLine1"), Value(row, "AddressLine2"),
                    Value(row, "City"), Value(row, "Region"), Value(row, "PostalCode"), Value(row, "CountryCode"),
                    Value(row, "Notes")))).ToList();

            return Ok(await import.Handle(rows, ct));
        }

        private static IActionResult ExportFile(string prefix, string sheet, string format,
            IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
        {
            var date = DateTime.UtcNow.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            return format.Trim().ToLowerInvariant() switch
            {
                "csv" => new FileContentResult(SpreadsheetFiles.WriteCsv(headers, rows), "text/csv; charset=utf-8")
                    { FileDownloadName = $"{prefix}-{date}.csv" },
                "xlsx" => new FileContentResult(SpreadsheetFiles.WriteExcel(sheet, headers, rows),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
                    { FileDownloadName = $"{prefix}-{date}.xlsx" },
                _ => new BadRequestObjectResult("Format must be csv or xlsx.")
            };
        }
    }
}
