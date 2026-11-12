using Application.Reports;
using Domain.Bills;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class PayablesRepository(AppDbContext context) : IPayablesRepository
{
    public async Task<IReadOnlyList<OpenBillRow>> GetOpenBillsAsync(
        Guid? supplierId, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from b in context.SupplierBills.AsNoTracking()
            join s in context.Suppliers.AsNoTracking() on b.SupplierId equals s.Id
            where (b.Status == BillStatus.Open || b.Status == BillStatus.PartiallyPaid)
                  && (supplierId == null || b.SupplierId == supplierId)
            select new { b.Id, b.Number, b.SupplierId, SupplierName = s.Name, b.DueDate, b.Total, b.AmountPaid, b.ExchangeRateToBase })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new OpenBillRow(
            r.Id, r.Number, r.SupplierId, r.SupplierName, r.DueDate,
            decimal.Round((r.Total - r.AmountPaid) * r.ExchangeRateToBase, 2))).ToList();
    }

    public async Task<SupplierTotals> GetSupplierTotalsAsync(
        Guid supplierId, CancellationToken cancellationToken = default)
    {
        // Drafts and voided bills are not money owed, so they are not counted.
        var counted = context.SupplierBills.AsNoTracking().Where(b =>
            b.SupplierId == supplierId &&
            (b.Status == BillStatus.Open || b.Status == BillStatus.PartiallyPaid || b.Status == BillStatus.Paid));

        var rows = await counted.Select(b => new { b.Total, b.AmountPaid, b.ExchangeRateToBase }).ToListAsync(cancellationToken);
        var billed = rows.Sum(b => decimal.Round(b.Total * b.ExchangeRateToBase, 2));
        var paymentRows = await context.BillPayments.AsNoTracking()
            .Where(p => p.SupplierId == supplierId && p.ReversedAt == null)
            .Select(p => new { p.Amount, p.ExchangeRateToBase })
            .ToListAsync(cancellationToken);
        var paid = paymentRows.Sum(p => decimal.Round(p.Amount * p.ExchangeRateToBase, 2));
        return new SupplierTotals(billed, paid);
    }

    public async Task<IReadOnlyList<BilledRow>> GetBilledAsync(
    DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from b in context.SupplierBills.AsNoTracking()
            join s in context.Suppliers.AsNoTracking() on b.SupplierId equals s.Id
            where (b.Status == BillStatus.Open || b.Status == BillStatus.PartiallyPaid || b.Status == BillStatus.Paid)
                  && b.BillDate >= fromDate && b.BillDate <= toDate
            select new { b.SupplierId, SupplierName = s.Name, b.BillDate, b.Subtotal, b.DiscountTotal, b.TaxTotal, b.Total, b.ExchangeRateToBase })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new BilledRow(
            r.SupplierId, r.SupplierName, r.BillDate,
            decimal.Round((r.Subtotal - r.DiscountTotal) * r.ExchangeRateToBase, 2),
            decimal.Round(r.TaxTotal * r.ExchangeRateToBase, 2),
            decimal.Round(r.Total * r.ExchangeRateToBase, 2))).ToList();
    }

    public async Task<IReadOnlyList<DatedAmount>> GetPaidAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var rows = await context.BillPayments.AsNoTracking()
            .Where(p => p.ReversedAt == null && p.PaidOn >= from && p.PaidOn <= to)
            .Select(p => new { p.PaidOn, p.Amount, p.ExchangeRateToBase })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new DatedAmount(r.PaidOn, decimal.Round(r.Amount * r.ExchangeRateToBase, 2))).ToList();
    }
}
