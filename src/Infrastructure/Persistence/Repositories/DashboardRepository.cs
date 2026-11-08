using Application.Reports;
using Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class DashboardRepository(AppDbContext context) : IDashboardRepository
{
    public async Task<IReadOnlyList<SalesRow>> GetSalesAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        var rows = await context.Invoices.AsNoTracking()
            .Where(i => (i.Status == InvoiceStatus.Issued
                         || i.Status == InvoiceStatus.PartiallyPaid
                         || i.Status == InvoiceStatus.Paid)
                        && i.IssueDate >= fromDate && i.IssueDate <= toDate)
            .Select(i => new { i.IssueDate, i.Subtotal, i.DiscountTotal, i.TaxTotal, i.Total })
            .ToListAsync(cancellationToken);

        // An issued invoice always has an issue date. Net means before tax and after discount.
        return rows.Select(r => new SalesRow(
            r.IssueDate!.Value, r.Subtotal - r.DiscountTotal, r.TaxTotal, r.Total)).ToList();
    }

    public async Task<IReadOnlyList<DatedAmount>> GetCollectedAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        var rows = await context.Payments.AsNoTracking()
            .Where(p => p.ReversedAt == null && p.PaidOn >= fromDate && p.PaidOn <= toDate)
            .Select(p => new { p.PaidOn, p.Amount })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new DatedAmount(r.PaidOn, r.Amount)).ToList();
    }

    public async Task<IReadOnlyDictionary<InvoiceStatus, int>> GetStatusCountsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await context.Invoices.AsNoTracking()
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Status, r => r.Count);
    }
}