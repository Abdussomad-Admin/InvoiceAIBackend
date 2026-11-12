using Application.Reports;
using Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class ReceivablesRepository(AppDbContext context) : IReceivablesRepository
{
    public async Task<IReadOnlyList<OpenInvoiceRow>> GetOpenInvoicesAsync(
        Guid? customerId, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from i in context.Invoices.AsNoTracking()
            join c in context.Customers.AsNoTracking() on i.CustomerId equals c.Id
            where (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid)
                  && (customerId == null || i.CustomerId == customerId)
            select new { i.Id, i.Number, i.CustomerId, CustomerName = c.Name, i.DueDate, i.CurrencyCode,
                i.Total, i.AmountPaid, i.AmountCredited, i.ExchangeRateToBase })
            .ToListAsync(cancellationToken);

        // An issued invoice always has a due date, set when it was issued.
        return rows.Select(r => new OpenInvoiceRow(
            r.Id, r.Number, r.CustomerId, r.CustomerName, r.DueDate!.Value, r.CurrencyCode,
            r.Total - r.AmountPaid - r.AmountCredited,
            decimal.Round(Math.Max(0m, r.Total - r.AmountPaid - r.AmountCredited) * r.ExchangeRateToBase, 2))).ToList();
    }

    public async Task<CustomerTotals> GetCustomerTotalsAsync(
        Guid customerId, CancellationToken cancellationToken = default)
    {
        var counted = context.Invoices.AsNoTracking().Where(i =>
            i.CustomerId == customerId &&
            (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid || i.Status == InvoiceStatus.Paid));

        var values = await counted.Select(i => new { i.Id, i.Total, i.ExchangeRateToBase }).ToListAsync(cancellationToken);
        var invoiced = values.Sum(i => decimal.Round(i.Total * i.ExchangeRateToBase, 2));
        var invoiceIds = values.Select(i => i.Id).ToList();
        var paymentRows = await context.Payments.AsNoTracking()
            .Where(p => invoiceIds.Contains(p.InvoiceId) && p.ReversedAt == null)
            .Select(p => new { p.Amount, p.ExchangeRateToBase }).ToListAsync(cancellationToken);
        var paid = paymentRows.Sum(p => decimal.Round(p.Amount * p.ExchangeRateToBase, 2));
        return new CustomerTotals(invoiced, paid);
    }
}
