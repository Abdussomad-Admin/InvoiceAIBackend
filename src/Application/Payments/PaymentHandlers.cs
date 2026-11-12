using Application.Common;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Customers;
using Application.Invoices;
using Application.Organizations;
using Domain.Common;
using Domain.Payments;

namespace Application.Payments;

internal static class PaymentMapping
{
    public static PaymentDto ToDto(this Payment p, string? invoiceNumber, string customerName) => new(
        p.Id, p.InvoiceId, invoiceNumber, p.CustomerId, customerName, p.Amount, p.CurrencyCode, p.ExchangeRateToBase,
        p.PaidOn, p.Method, p.Reference, p.Notes, p.RecordedByUserId, p.CreatedAt,
        p.IsReversed, p.ReversedAt, p.ReversalReason);
}

public sealed class RecordPaymentHandler(
    IInvoiceRepository invoices, IPaymentRepository payments, ICustomerRepository customers,
    IOrganizationRepository organizations, ITransactionRunner transactions, IUnitOfWork unitOfWork,
    ICurrentOrganization currentOrganization, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<PaymentDto> Handle(Guid invoiceId, RecordPaymentCommand command, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);

        var today = InvoiceLookups.Today(organization, clock);
        var paidOn = command.PaidOn ?? today;
        if (paidOn > today)
            throw new DomainException("The payment date cannot be in the future.");

        var reference = string.IsNullOrWhiteSpace(command.Reference) ? null : command.Reference.Trim();

        Payment? payment = null;
        string? invoiceNumber = null;

        await transactions.RunInTransactionAsync(async () =>
        {
            // Lock first, then load: a second payment on the same invoice waits here until this one
            // commits, and then sees the updated balance. That is what stops overpayment.
            if (!await invoices.LockAsync(invoiceId, cancellationToken))
                throw new NotFoundException("Invoice not found.");

            var invoice = await invoices.GetByIdAsync(invoiceId, cancellationToken)
                ?? throw new NotFoundException("Invoice not found.");

            if (reference is not null && await payments.ReferenceInUseAsync(invoiceId, reference, cancellationToken))
                throw new ConflictException("A payment with this reference is already recorded for this invoice.");

            invoice.ApplyPayment(command.Amount);

            var exchangeRate = invoice.CurrencyCode == invoice.BaseCurrencyCode
                ? 1m
                : command.ExchangeRateToBase ?? throw new DomainException(
                    $"Enter the {invoice.CurrencyCode} to {invoice.BaseCurrencyCode} exchange rate for this payment.");
            payment = Payment.Create(
                organization.Id, invoice.Id, invoice.CustomerId, invoice.CurrencyCode, command.Amount,
                paidOn, command.Method, reference, command.Notes, actorId, exchangeRate);
            payments.Add(payment);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            invoiceNumber = invoice.Number;
        }, cancellationToken);

        var customerName = await InvoiceLookups.CustomerNameAsync(customers, payment!.CustomerId, cancellationToken);
        return payment.ToDto(invoiceNumber, customerName);
    }
}

public sealed class ReversePaymentHandler(
    IInvoiceRepository invoices, IPaymentRepository payments, ICustomerRepository customers,
    ITransactionRunner transactions, IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<PaymentDto> Handle(Guid paymentId, ReversePaymentCommand command, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");

        // Only the invoice id is read before locking, so the payment itself is loaded fresh afterwards.
        var invoiceId = await payments.GetInvoiceIdAsync(paymentId, cancellationToken)
            ?? throw new NotFoundException("Payment not found.");

        Payment? payment = null;
        string? invoiceNumber = null;

        await transactions.RunInTransactionAsync(async () =>
        {
            if (!await invoices.LockAsync(invoiceId, cancellationToken))
                throw new NotFoundException("Invoice not found.");

            payment = await payments.GetByIdAsync(paymentId, cancellationToken)
                ?? throw new NotFoundException("Payment not found.");
            var invoice = await invoices.GetByIdAsync(invoiceId, cancellationToken)
                ?? throw new NotFoundException("Invoice not found.");

            payment.Reverse(command.Reason, actorId, clock.GetUtcNow());
            invoice.ReversePayment(payment.Amount);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            invoiceNumber = invoice.Number;
        }, cancellationToken);

        var customerName = await InvoiceLookups.CustomerNameAsync(customers, payment!.CustomerId, cancellationToken);
        return payment.ToDto(invoiceNumber, customerName);
    }
}

public sealed class ListPaymentsHandler(IPaymentRepository payments)
{
    public async Task<PagedResult<PaymentDto>> Handle(ListPaymentsQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, total) = await payments.SearchAsync(
            new PaymentSearch(query.InvoiceId, query.CustomerId, query.From, query.To, query.IncludeReversed),
            (page - 1) * pageSize, pageSize, cancellationToken);

        return new PagedResult<PaymentDto>(items, total, page, pageSize);
    }
}
