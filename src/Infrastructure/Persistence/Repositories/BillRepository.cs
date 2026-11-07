using Application.Bills;
using Domain.Bills;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class BillRepository(AppDbContext context) : IBillRepository
{
    public Task<SupplierBill?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => context.SupplierBills.Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public Task<bool> NumberInUseAsync(
        Guid supplierId, string numberKey, Guid excludeBillId, CancellationToken cancellationToken = default)
        => context.SupplierBills.AnyAsync(
            b => b.SupplierId == supplierId && b.NumberKey == numberKey
                 && b.Status != BillStatus.Void && b.Id != excludeBillId,
            cancellationToken);

    public async Task<IReadOnlyList<PossibleDuplicateDto>> FindPossibleDuplicatesAsync(
        Guid supplierId, decimal total, DateOnly billDate, Guid excludeBillId,
        CancellationToken cancellationToken = default)
    {
        var rows = await context.SupplierBills.AsNoTracking()
            .Where(b => b.SupplierId == supplierId && b.Total == total && b.BillDate == billDate
                        && b.Status != BillStatus.Void && b.Id != excludeBillId)
            .Select(b => new { b.Id, b.Number, b.BillDate, b.Total })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new PossibleDuplicateDto(
            r.Id, r.Number, r.BillDate, r.Total, "Same supplier, bill date and total")).ToList();
    }

    public async Task<(IReadOnlyList<BillSummaryDto> Items, int Total)> SearchAsync(
        BillSearch filter, DateOnly today, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query =
            from b in context.SupplierBills.AsNoTracking()
            join s in context.Suppliers.AsNoTracking() on b.SupplierId equals s.Id
            select new { Bill = b, SupplierName = s.Name };

        if (filter.Status is { } status)
            query = query.Where(x => x.Bill.Status == status);

        if (filter.SupplierId is { } supplierId)
            query = query.Where(x => x.Bill.SupplierId == supplierId);

        if (filter.Overdue == true)
            query = query.Where(x =>
                (x.Bill.Status == BillStatus.Open || x.Bill.Status == BillStatus.PartiallyPaid)
                && x.Bill.DueDate < today);

        if (filter.DueBy is { } dueBy)
            query = query.Where(x =>
                (x.Bill.Status == BillStatus.Open || x.Bill.Status == BillStatus.PartiallyPaid)
                && x.Bill.DueDate <= dueBy);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var escaped = filter.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = $"%{escaped}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.Bill.Number, pattern) || EF.Functions.ILike(x.SupplierName, pattern));
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(x => x.Bill.DueDate).ThenByDescending(x => x.Bill.CreatedAt)
            .Skip(skip).Take(take)
            .Select(x => new
            {
                x.Bill.Id,
                x.Bill.Number,
                x.Bill.SupplierId,
                x.SupplierName,
                x.Bill.CurrencyCode,
                x.Bill.Status,
                x.Bill.BillDate,
                x.Bill.DueDate,
                x.Bill.Total,
                x.Bill.AmountPaid
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new BillSummaryDto(
            r.Id, r.Number, r.SupplierId, r.SupplierName, r.CurrencyCode, r.Status, r.BillDate, r.DueDate,
            r.Total, r.AmountPaid, r.Total - r.AmountPaid,
            r.Status is BillStatus.Open or BillStatus.PartiallyPaid && r.DueDate < today)).ToList();

        return (items, total);
    }

    public async Task<bool> LockAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // The filtered query first, so another organization's bill is never even locked.
        if (!await context.SupplierBills.AsNoTracking().AnyAsync(b => b.Id == id, cancellationToken))
            return false;

        await context.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM "SupplierBills" WHERE "Id" = {id} FOR UPDATE""", cancellationToken);
        return true;
    }

    public void Add(SupplierBill bill) => context.SupplierBills.Add(bill);
}