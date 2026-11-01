using Application.Common;
using Application.Customers;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Host.Controllers
{
    [ApiController]
    [Route("api/customers")]
    public class CustomersController(
        CreateCustomerHandler create,
        UpdateCustomerHandler update,
        ArchiveCustomerHandler archive,
        GetCustomerHandler get,
        ListCustomersHandler list) : ControllerBase
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
    }
}