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
            select new { i.Id, i.Number, i.CustomerId, CustomerName = c.Name, i.DueDate, i.Total, i.AmountPaid })
            .ToListAsync(cancellationToken);

        // An issued invoice always has a due date, set when it was issued.
        return rows.Select(r => new OpenInvoiceRow(
            r.Id, r.Number, r.CustomerId, r.CustomerName, r.DueDate!.Value, r.Total - r.AmountPaid)).ToList();
    }

    public async Task<CustomerTotals> GetCustomerTotalsAsync(
        Guid customerId, CancellationToken cancellationToken = default)
    {
        var counted = context.Invoices.AsNoTracking().Where(i =>
            i.CustomerId == customerId &&
            (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid || i.Status == InvoiceStatus.Paid));

        var invoiced = await counted.SumAsync(i => (decimal?)i.Total, cancellationToken) ?? 0m;
        var paid = await counted.SumAsync(i => (decimal?)i.AmountPaid, cancellationToken) ?? 0m;
        return new CustomerTotals(invoiced, paid);
    }
}