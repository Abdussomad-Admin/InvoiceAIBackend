using Application.Bills;
using Domain.Bills;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class BillPaymentRepository(AppDbContext context) : IBillPaymentRepository
{
    public Task<BillPayment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => context.BillPayments.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Guid?> GetBillIdAsync(Guid paymentId, CancellationToken cancellationToken = default)
        => await context.BillPayments.AsNoTracking()
            .Where(p => p.Id == paymentId)
            .Select(p => (Guid?)p.BillId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ReferenceInUseAsync(Guid billId, string reference, CancellationToken cancellationToken = default)
        => context.BillPayments.AnyAsync(
            p => p.BillId == billId && p.Reference == reference && p.ReversedAt == null, cancellationToken);

    public async Task<(IReadOnlyList<BillPaymentDto> Items, int Total)> SearchAsync(
        BillPaymentSearch filter, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query =
            from p in context.BillPayments.AsNoTracking()
            join b in context.SupplierBills.AsNoTracking() on p.BillId equals b.Id
            join s in context.Suppliers.AsNoTracking() on p.SupplierId equals s.Id
            select new { Payment = p, BillNumber = b.Number, SupplierName = s.Name };

        if (filter.BillId is { } billId)
            query = query.Where(x => x.Payment.BillId == billId);
        if (filter.SupplierId is { } supplierId)
            query = query.Where(x => x.Payment.SupplierId == supplierId);
        if (filter.From is { } from)
            query = query.Where(x => x.Payment.PaidOn >= from);
        if (filter.To is { } to)
            query = query.Where(x => x.Payment.PaidOn <= to);
        if (!filter.IncludeReversed)
            query = query.Where(x => x.Payment.ReversedAt == null);

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(x => x.Payment.PaidOn)
            .ThenByDescending(x => x.Payment.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new BillPaymentDto(
            x.Payment.Id, x.Payment.BillId, x.BillNumber, x.Payment.SupplierId, x.SupplierName,
            x.Payment.Amount, x.Payment.CurrencyCode, x.Payment.PaidOn, x.Payment.Method,
            x.Payment.Reference, x.Payment.Notes, x.Payment.RecordedByUserId, x.Payment.CreatedAt,
            x.Payment.IsReversed, x.Payment.ReversedAt, x.Payment.ReversalReason)).ToList();

        return (items, total);
    }

    public void Add(BillPayment payment) => context.BillPayments.Add(payment);
}