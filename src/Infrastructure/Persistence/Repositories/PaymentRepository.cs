using Application.Payments;
using Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class PaymentRepository(AppDbContext context) : IPaymentRepository
{
    public Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => context.Payments.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<Guid?> GetInvoiceIdAsync(Guid paymentId, CancellationToken cancellationToken = default)
        => await context.Payments.AsNoTracking()
            .Where(p => p.Id == paymentId)
            .Select(p => (Guid?)p.InvoiceId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ReferenceInUseAsync(Guid invoiceId, string reference, CancellationToken cancellationToken = default)
        => context.Payments.AnyAsync(
            p => p.InvoiceId == invoiceId && p.Reference == reference && p.ReversedAt == null, cancellationToken);

    public async Task<(IReadOnlyList<PaymentDto> Items, int Total)> SearchAsync(
        PaymentSearch filter, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query =
            from p in context.Payments.AsNoTracking()
            join i in context.Invoices.AsNoTracking() on p.InvoiceId equals i.Id
            join c in context.Customers.AsNoTracking() on p.CustomerId equals c.Id
            select new { Payment = p, InvoiceNumber = i.Number, CustomerName = c.Name };

        if (filter.InvoiceId is { } invoiceId)
            query = query.Where(x => x.Payment.InvoiceId == invoiceId);
        if (filter.CustomerId is { } customerId)
            query = query.Where(x => x.Payment.CustomerId == customerId);
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

        var items = rows.Select(x => new PaymentDto(
            x.Payment.Id, x.Payment.InvoiceId, x.InvoiceNumber, x.Payment.CustomerId, x.CustomerName,
            x.Payment.Amount, x.Payment.CurrencyCode, x.Payment.ExchangeRateToBase, x.Payment.PaidOn, x.Payment.Method,
            x.Payment.Reference, x.Payment.Notes, x.Payment.RecordedByUserId, x.Payment.CreatedAt,
            x.Payment.IsReversed, x.Payment.ReversedAt, x.Payment.ReversalReason)).ToList();

        return (items, total);
    }

    public void Add(Payment payment) => context.Payments.Add(payment);
}
