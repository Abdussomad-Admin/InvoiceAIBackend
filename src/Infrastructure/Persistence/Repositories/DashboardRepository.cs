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
                         || i.Status == InvoiceStatus.Paid
                         || i.Status == InvoiceStatus.Credited)
                        && i.IssueDate >= fromDate && i.IssueDate <= toDate)
            .Select(i => new { i.Id, i.IssueDate, i.Subtotal, i.DiscountTotal, i.TaxTotal, i.Total,
                i.ExchangeRateToBase })
            .ToListAsync(cancellationToken);

        // An issued invoice always has an issue date. Net means before tax and after discount.
        var sales = rows.Select(r =>
        {
            var net = r.Subtotal - r.DiscountTotal;
            return new SalesRow(r.IssueDate!.Value,
                decimal.Round(net * r.ExchangeRateToBase, 2),
                decimal.Round(r.TaxTotal * r.ExchangeRateToBase, 2),
                decimal.Round(r.Total * r.ExchangeRateToBase, 2));
        }).ToList();

        // Recognize each credit on the credit note's issue date, using the invoice's
        // original rate snapshot so a correction does not invent an FX gain or loss.
        var credits = await (from credit in context.CreditNotes.AsNoTracking()
                             join invoice in context.Invoices.AsNoTracking() on credit.InvoiceId equals invoice.Id
                             where credit.IssuedOn >= fromDate && credit.IssuedOn <= toDate
                             select new { credit.IssuedOn, credit.Amount, credit.ExchangeRateToBase,
                                 invoice.Total, Net = invoice.Subtotal - invoice.DiscountTotal, invoice.TaxTotal })
            .ToListAsync(cancellationToken);
        sales.AddRange(credits.Select(credit =>
        {
            var netShare = credit.Total == 0m ? 0m : credit.Net / credit.Total;
            var taxShare = credit.Total == 0m ? 0m : credit.TaxTotal / credit.Total;
            return new SalesRow(credit.IssuedOn,
                -decimal.Round(credit.Amount * netShare * credit.ExchangeRateToBase, 2),
                -decimal.Round(credit.Amount * taxShare * credit.ExchangeRateToBase, 2),
                -decimal.Round(credit.Amount * credit.ExchangeRateToBase, 2));
        }));
        return sales;
    }

    public async Task<IReadOnlyList<DatedAmount>> GetCollectedAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        var rows = await context.Payments.AsNoTracking()
            .Where(p => p.ReversedAt == null && p.PaidOn >= fromDate && p.PaidOn <= toDate)
            .Select(p => new { p.PaidOn, p.Amount, p.ExchangeRateToBase })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new DatedAmount(r.PaidOn, decimal.Round(r.Amount * r.ExchangeRateToBase, 2))).ToList();
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
