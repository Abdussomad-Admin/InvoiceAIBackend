using Application.Invoices;
using Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class RecurringInvoiceRepository(AppDbContext context) : IRecurringInvoiceRepository
{
    public async Task<IReadOnlyList<RecurringInvoiceDto>> ListAsync(CancellationToken ct)
        => await (from schedule in context.RecurringInvoiceSchedules.AsNoTracking()
                  join customer in context.Customers.AsNoTracking() on schedule.CustomerId equals customer.Id
                  orderby schedule.IsActive descending, schedule.NextRunOn
                  select new RecurringInvoiceDto(schedule.Id, schedule.CustomerId, customer.Name,
                      schedule.Description, schedule.CurrencyCode, schedule.BaseCurrencyCode, schedule.ExchangeRateToBase,
                      schedule.Frequency, schedule.NextRunOn,
                      schedule.PaymentTermsDays, schedule.IsActive, schedule.LastGeneratedAt))
            .ToListAsync(ct);

    public Task<RecurringInvoiceSchedule?> GetAsync(Guid id, CancellationToken ct)
        => context.RecurringInvoiceSchedules.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void Add(RecurringInvoiceSchedule schedule) => context.RecurringInvoiceSchedules.Add(schedule);

    public async Task<IReadOnlyList<RecurringInvoiceSchedule>> DueAsync(DateOnly today, CancellationToken ct)
        => await context.RecurringInvoiceSchedules.IgnoreQueryFilters()
            .Where(x => x.IsActive && x.NextRunOn <= today).OrderBy(x => x.NextRunOn)
            .Take(100).ToListAsync(ct);
}
