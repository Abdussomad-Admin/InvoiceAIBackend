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
            select new { b.Id, b.Number, b.SupplierId, SupplierName = s.Name, b.DueDate, b.Total, b.AmountPaid })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new OpenBillRow(
            r.Id, r.Number, r.SupplierId, r.SupplierName, r.DueDate, r.Total - r.AmountPaid)).ToList();
    }

    public async Task<SupplierTotals> GetSupplierTotalsAsync(
        Guid supplierId, CancellationToken cancellationToken = default)
    {
        // Drafts and voided bills are not money owed, so they are not counted.
        var counted = context.SupplierBills.AsNoTracking().Where(b =>
            b.SupplierId == supplierId &&
            (b.Status == BillStatus.Open || b.Status == BillStatus.PartiallyPaid || b.Status == BillStatus.Paid));

        var billed = await counted.SumAsync(b => (decimal?)b.Total, cancellationToken) ?? 0m;
        var paid = await counted.SumAsync(b => (decimal?)b.AmountPaid, cancellationToken) ?? 0m;
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
            select new { b.SupplierId, SupplierName = s.Name, b.BillDate, b.Subtotal, b.DiscountTotal, b.TaxTotal, b.Total })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new BilledRow(
            r.SupplierId, r.SupplierName, r.BillDate, r.Subtotal - r.DiscountTotal, r.TaxTotal, r.Total)).ToList();
    }

    public async Task<IReadOnlyList<DatedAmount>> GetPaidAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var rows = await context.BillPayments.AsNoTracking()
            .Where(p => p.ReversedAt == null && p.PaidOn >= from && p.PaidOn <= to)
            .Select(p => new { p.PaidOn, p.Amount })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new DatedAmount(r.PaidOn, r.Amount)).ToList();
    }
}