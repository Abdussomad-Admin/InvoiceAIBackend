using Application.Invoices;
using Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class InvoiceRepository(AppDbContext context) : IInvoiceRepository
{
    public Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => context.Invoices.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<InvoiceSummaryDto> Items, int Total)> SearchAsync(
        InvoiceSearch filter, DateOnly today, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query =
            from i in context.Invoices.AsNoTracking()
            join c in context.Customers.AsNoTracking() on i.CustomerId equals c.Id
            select new { Invoice = i, CustomerName = c.Name };

        if (filter.Status is { } status)
            query = query.Where(x => x.Invoice.Status == status);

        if (filter.CustomerId is { } customerId)
            query = query.Where(x => x.Invoice.CustomerId == customerId);

        if (filter.Overdue == true)
            query = query.Where(x =>
                (x.Invoice.Status == InvoiceStatus.Issued || x.Invoice.Status == InvoiceStatus.PartiallyPaid)
                && x.Invoice.DueDate < today);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var escaped = filter.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = $"%{escaped}%";
            query = query.Where(x =>
                (x.Invoice.Number != null && EF.Functions.ILike(x.Invoice.Number, pattern)) ||
                EF.Functions.ILike(x.CustomerName, pattern));
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(x => x.Invoice.CreatedAt)
            .Skip(skip).Take(take)
            .Select(x => new
            {
                x.Invoice.Id,
                x.Invoice.Number,
                x.Invoice.CustomerId,
                x.CustomerName,
                x.Invoice.CurrencyCode,
                x.Invoice.Status,
                x.Invoice.IssueDate,
                x.Invoice.DueDate,
                x.Invoice.Total,
                x.Invoice.AmountPaid
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new InvoiceSummaryDto(
            r.Id, r.Number, r.CustomerId, r.CustomerName, r.CurrencyCode, r.Status,
            r.IssueDate, r.DueDate, r.Total, r.AmountPaid, r.Total - r.AmountPaid,
            r.Status is InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid
                && r.DueDate is { } due && due < today)).ToList();

        return (items, total);
    }

    public void Add(Invoice invoice) => context.Invoices.Add(invoice);

    public async Task<IReadOnlyList<InvoiceExportRow>> ExportAsync(CancellationToken cancellationToken = default)
        => await (from invoice in context.Invoices.AsNoTracking()
                  join customer in context.Customers.AsNoTracking() on invoice.CustomerId equals customer.Id
                  orderby invoice.CreatedAt descending
                  select new InvoiceExportRow(invoice.Number, customer.Name, invoice.CurrencyCode,
                      invoice.Status, invoice.IssueDate, invoice.DueDate, invoice.Total,
                      invoice.AmountPaid, invoice.Total - invoice.AmountPaid, invoice.CreatedAt))
            .Take(50_001)
            .ToListAsync(cancellationToken);

    public async Task<bool> LockAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // The filtered query first, so another organization's invoice is never even locked.
        if (!await context.Invoices.AsNoTracking().AnyAsync(i => i.Id == id, cancellationToken))
            return false;

        await context.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM "Invoices" WHERE "Id" = {id} FOR UPDATE""", cancellationToken);
        return true;
    }
}
