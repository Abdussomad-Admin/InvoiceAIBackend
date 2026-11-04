using Application.Common;
using Application.Payments;
using Domain.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Host.Controllers;

[ApiController]
public class PaymentsController(
    RecordPaymentHandler record,
    ReversePaymentHandler reverse,
    ListPaymentsHandler list) : ControllerBase
{
    [HttpPost("api/invoices/{invoiceId:guid}/payments")]
    [Authorize(Policy = nameof(Permission.RecordPayments))]
    public async Task<ActionResult<PaymentDto>> Record(Guid invoiceId, RecordPaymentCommand command, CancellationToken ct)
        => Ok(await record.Handle(invoiceId, command, ct));

    [HttpGet("api/invoices/{invoiceId:guid}/payments")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<PagedResult<PaymentDto>>> ForInvoice(Guid invoiceId, CancellationToken ct)
        => Ok(await list.Handle(new ListPaymentsQuery(invoiceId, null, null, null, true, 1, 100), ct));

    [HttpGet("api/payments")]
    [Authorize(Policy = nameof(Permission.ViewData))]
    public async Task<ActionResult<PagedResult<PaymentDto>>> List(
        [FromQuery] Guid? customerId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] bool includeReversed = true, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await list.Handle(new ListPaymentsQuery(null, customerId, from, to, includeReversed, page, pageSize), ct));

    [HttpPost("api/payments/{id:guid}/reverse")]
    [Authorize(Policy = nameof(Permission.RecordPayments))]
    public async Task<ActionResult<PaymentDto>> Reverse(Guid id, ReversePaymentCommand command, CancellationToken ct)
        => Ok(await reverse.Handle(id, command, ct));
}