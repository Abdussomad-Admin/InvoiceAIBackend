using Application.Customers;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class CustomerStatementRepository(AppDbContext context) : ICustomerStatementRepository
{
    public async Task<CustomerStatementData> GetAsync(
        Guid customerId, DateOnly from, DateOnly to, DateTimeOffset startUtc,
        DateTimeOffset endExclusiveUtc, CancellationToken cancellationToken = default)
    {
        var statementFrom = from;
        var invoiceQuery = context.Invoices.AsNoTracking()
            .Where(i => i.CustomerId == customerId && i.IssueDate != null);
        var paymentQuery = context.Payments.AsNoTracking()
            .Where(p => p.CustomerId == customerId);

        var invoicesBefore = await invoiceQuery
            .Where(i => i.IssueDate < from)
            .Select(i => (decimal?)(i.Total * i.ExchangeRateToBase))
            .SumAsync(cancellationToken) ?? 0m;
        var voidedBefore = await invoiceQuery
            .Where(i => i.VoidedAt != null && i.VoidedAt < startUtc)
            .Select(i => (decimal?)(i.Total * i.ExchangeRateToBase))
            .SumAsync(cancellationToken) ?? 0m;
        var paymentsBefore = await paymentQuery
            .Where(p => p.PaidOn < from)
            .Select(p => (decimal?)(p.Amount * p.ExchangeRateToBase))
            .SumAsync(cancellationToken) ?? 0m;
        var reversalsBefore = await paymentQuery
            .Where(p => p.PaidOn < from && p.ReversedAt != null && p.ReversedAt < startUtc)
            .Select(p => (decimal?)(p.Amount * p.ExchangeRateToBase))
            .SumAsync(cancellationToken) ?? 0m;

        var priorCreditQuery = from credit in context.CreditNotes.AsNoTracking()
                               join invoice in context.Invoices.AsNoTracking() on credit.InvoiceId equals invoice.Id
                               where invoice.CustomerId == customerId && credit.IssuedOn < statementFrom
                               select (decimal?)(credit.Amount * credit.ExchangeRateToBase);
        var creditsBefore = await priorCreditQuery.SumAsync(cancellationToken) ?? 0m;

        var invoices = await invoiceQuery
            .Where(i => (i.IssueDate >= from && i.IssueDate <= to)
                        || (i.VoidedAt != null && i.VoidedAt >= startUtc && i.VoidedAt < endExclusiveUtc))
            .Select(i => new CustomerStatementInvoiceRow(
                i.Id, i.Number, i.IssueDate, i.Total * i.ExchangeRateToBase, i.CurrencyCode, i.VoidedAt))
            .ToListAsync(cancellationToken);

        var payments = await paymentQuery
            .Where(p => (p.PaidOn >= from && p.PaidOn <= to)
                        || (p.ReversedAt != null && p.ReversedAt >= startUtc && p.ReversedAt < endExclusiveUtc))
            .Select(p => new CustomerStatementPaymentRow(
                p.Id, p.PaidOn, p.Amount * p.ExchangeRateToBase, p.CurrencyCode, p.Method, p.Reference, p.ReversedAt))
            .ToListAsync(cancellationToken);

        var creditQuery = from credit in context.CreditNotes.AsNoTracking()
                          join invoice in context.Invoices.AsNoTracking() on credit.InvoiceId equals invoice.Id
                          where invoice.CustomerId == customerId && credit.IssuedOn >= statementFrom && credit.IssuedOn <= to
                          select new CustomerStatementCreditNoteRow(credit.Number,
                              credit.Amount * credit.ExchangeRateToBase, credit.IssuedOn, credit.Reason);
        var credits = await creditQuery.ToListAsync(cancellationToken);

        return new CustomerStatementData(
            new CustomerStatementOpeningParts(invoicesBefore, voidedBefore, paymentsBefore, reversalsBefore, creditsBefore),
            invoices, payments, credits);
    }
}
