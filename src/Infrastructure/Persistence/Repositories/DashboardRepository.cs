using Application.Reports;
using Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class DashboardRepository(AppDbContext context) : IDashboardRepository
{
    public async Task<IReadOnlyList<DatedAmount>> GetInvoicedAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var rows = await context.Invoices.AsNoTracking()
            .Where(i => (i.Status == InvoiceStatus.Issued
                         || i.Status == InvoiceStatus.PartiallyPaid
                         || i.Status == InvoiceStatus.Paid)
                        && i.IssueDate >= from && i.IssueDate <= to)
            .Select(i => new { i.IssueDate, i.Total })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new DatedAmount(r.IssueDate!.Value, r.Total)).ToList();
    }

    public async Task<IReadOnlyList<DatedAmount>> GetCollectedAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var rows = await context.Payments.AsNoTracking()
            .Where(p => p.ReversedAt == null && p.PaidOn >= from && p.PaidOn <= to)
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