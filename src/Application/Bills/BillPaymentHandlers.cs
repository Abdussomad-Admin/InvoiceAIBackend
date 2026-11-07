using Application.Common;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Invoices;
using Application.Organizations;
using Application.Suppliers;
using Domain.Bills;
using Domain.Common;

namespace Application.Bills;

internal static class BillPaymentMapping
{
    public static BillPaymentDto ToDto(this BillPayment p, string billNumber, string supplierName) => new(
        p.Id, p.BillId, billNumber, p.SupplierId, supplierName, p.Amount, p.CurrencyCode,
        p.PaidOn, p.Method, p.Reference, p.Notes, p.RecordedByUserId, p.CreatedAt,
        p.IsReversed, p.ReversedAt, p.ReversalReason);
}

public sealed class RecordBillPaymentHandler(
    IBillRepository bills, IBillPaymentRepository payments, ISupplierRepository suppliers,
    IOrganizationRepository organizations, ITransactionRunner transactions, IUnitOfWork unitOfWork,
    ICurrentOrganization currentOrganization, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<BillPaymentDto> Handle(
        Guid billId, RecordBillPaymentCommand command, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);

        var today = InvoiceLookups.Today(organization, clock);
        var paidOn = command.PaidOn ?? today;
        if (paidOn > today)
            throw new DomainException("The payment date cannot be in the future.");

        var reference = string.IsNullOrWhiteSpace(command.Reference) ? null : command.Reference.Trim();

        BillPayment? payment = null;
        string? billNumber = null;

        await transactions.RunInTransactionAsync(async () =>
        {
            // Lock first, then load: a second payment on the same bill waits here until this one commits,
            // and then sees the updated balance. That is what stops overpayment.
            if (!await bills.LockAsync(billId, cancellationToken))
                throw new NotFoundException("Bill not found.");

            var bill = await bills.GetByIdAsync(billId, cancellationToken)
                ?? throw new NotFoundException("Bill not found.");

            if (reference is not null && await payments.ReferenceInUseAsync(billId, reference, cancellationToken))
                throw new ConflictException("A payment with this reference is already recorded for this bill.");

            bill.ApplyPayment(command.Amount);

            payment = BillPayment.Create(
                organization.Id, bill.Id, bill.SupplierId, bill.CurrencyCode, command.Amount,
                paidOn, command.Method, reference, command.Notes, actorId);
            payments.Add(payment);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            billNumber = bill.Number;
        }, cancellationToken);

        var supplierName = await BillLookups.SupplierNameAsync(suppliers, payment!.SupplierId, cancellationToken);
        return payment.ToDto(billNumber!, supplierName);
    }
}

public sealed class ReverseBillPaymentHandler(
    IBillRepository bills, IBillPaymentRepository payments, ISupplierRepository suppliers,
    ITransactionRunner transactions, IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<BillPaymentDto> Handle(
        Guid paymentId, ReverseBillPaymentCommand command, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");

        var billId = await payments.GetBillIdAsync(paymentId, cancellationToken)
            ?? throw new NotFoundException("Payment not found.");

        BillPayment? payment = null;
        string? billNumber = null;

        await transactions.RunInTransactionAsync(async () =>
        {
            if (!await bills.LockAsync(billId, cancellationToken))
                throw new NotFoundException("Bill not found.");

            payment = await payments.GetByIdAsync(paymentId, cancellationToken)
                ?? throw new NotFoundException("Payment not found.");
            var bill = await bills.GetByIdAsync(billId, cancellationToken)
                ?? throw new NotFoundException("Bill not found.");

            payment.Reverse(command.Reason, actorId, clock.GetUtcNow());
            bill.ReversePayment(payment.Amount);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            billNumber = bill.Number;
        }, cancellationToken);

        var supplierName = await BillLookups.SupplierNameAsync(suppliers, payment!.SupplierId, cancellationToken);
        return payment.ToDto(billNumber!, supplierName);
    }
}

public sealed class ListBillPaymentsHandler(IBillPaymentRepository payments)
{
    public async Task<PagedResult<BillPaymentDto>> Handle(ListBillPaymentsQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, total) = await payments.SearchAsync(
            new BillPaymentSearch(query.BillId, query.SupplierId, query.From, query.To, query.IncludeReversed),
            (page - 1) * pageSize, pageSize, cancellationToken);

        return new PagedResult<BillPaymentDto>(items, total, page, pageSize);
    }
}