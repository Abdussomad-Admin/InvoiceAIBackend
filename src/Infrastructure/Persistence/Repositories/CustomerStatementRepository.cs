using Application.Customers;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class CustomerStatementRepository(AppDbContext context) : ICustomerStatementRepository
{
    public async Task<CustomerStatementData> GetAsync(
        Guid customerId, DateOnly from, DateOnly to, DateTimeOffset startUtc,
        DateTimeOffset endExclusiveUtc, CancellationToken cancellationToken = default)
    {
        var invoiceQuery = context.Invoices.AsNoTracking()
            .Where(i => i.CustomerId == customerId && i.IssueDate != null);
        var paymentQuery = context.Payments.AsNoTracking()
            .Where(p => p.CustomerId == customerId);

        var invoicesBefore = await invoiceQuery
            .Where(i => i.IssueDate < from)
            .Select(i => (decimal?)i.Total)
            .SumAsync(cancellationToken) ?? 0m;
        var voidedBefore = await invoiceQuery
            .Where(i => i.VoidedAt != null && i.VoidedAt < startUtc)
            .Select(i => (decimal?)i.Total)
            .SumAsync(cancellationToken) ?? 0m;
        var paymentsBefore = await paymentQuery
            .Where(p => p.PaidOn < from)
            .Select(p => (decimal?)p.Amount)
            .SumAsync(cancellationToken) ?? 0m;
        var reversalsBefore = await paymentQuery
            .Where(p => p.PaidOn < from && p.ReversedAt != null && p.ReversedAt < startUtc)
            .Select(p => (decimal?)p.Amount)
            .SumAsync(cancellationToken) ?? 0m;

        var invoices = await invoiceQuery
            .Where(i => (i.IssueDate >= from && i.IssueDate <= to)
                        || (i.VoidedAt != null && i.VoidedAt >= startUtc && i.VoidedAt < endExclusiveUtc))
            .Select(i => new CustomerStatementInvoiceRow(
                i.Id, i.Number, i.IssueDate, i.Total, i.CurrencyCode, i.VoidedAt))
            .ToListAsync(cancellationToken);

        var payments = await paymentQuery
            .Where(p => (p.PaidOn >= from && p.PaidOn <= to)
                        || (p.ReversedAt != null && p.ReversedAt >= startUtc && p.ReversedAt < endExclusiveUtc))
            .Select(p => new CustomerStatementPaymentRow(
                p.Id, p.PaidOn, p.Amount, p.CurrencyCode, p.Method, p.Reference, p.ReversedAt))
            .ToListAsync(cancellationToken);

        return new CustomerStatementData(
            new CustomerStatementOpeningParts(invoicesBefore, voidedBefore, paymentsBefore, reversalsBefore),
            invoices, payments);
    }
}
