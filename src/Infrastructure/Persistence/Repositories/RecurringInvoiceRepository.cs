using Application.Invoices;
using Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class RecurringInvoiceRepository(AppDbContext context) : IRecurringInvoiceRepository
{
    public async Task<IReadOnlyList<RecurringInvoiceDto>> ListAsync(CancellationToken ct)
    {
        var rows = await (from schedule in context.RecurringInvoiceSchedules.AsNoTracking()
                          join customer in context.Customers.AsNoTracking() on schedule.CustomerId equals customer.Id
                          orderby schedule.IsActive descending, schedule.NextRunOn
                          select new { Schedule = schedule, CustomerName = customer.Name })
            .ToListAsync(ct);
        return rows.Select(row => new RecurringInvoiceDto(row.Schedule.Id, row.Schedule.CustomerId,
            row.CustomerName, row.Schedule.Description, row.Schedule.CurrencyCode, row.Schedule.BaseCurrencyCode,
            row.Schedule.ExchangeRateToBase, row.Schedule.Frequency, row.Schedule.NextRunOn,
            row.Schedule.PaymentTermsDays, row.Schedule.IsActive, row.Schedule.LastGeneratedAt,
            row.Schedule.Lines().Select(line => new InvoiceLineInput(line.Description, line.Quantity, line.UnitPrice,
                line.DiscountPercent, line.TaxRatePercent)).ToList())).ToList();
    }

    public Task<RecurringInvoiceSchedule?> GetAsync(Guid id, CancellationToken ct)
        => context.RecurringInvoiceSchedules.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<bool> LockAsync(Guid id, CancellationToken ct)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"RecurringInvoiceSchedules\" WHERE \"Id\" = {id} FOR UPDATE", ct);
        return await context.RecurringInvoiceSchedules.AnyAsync(x => x.Id == id, ct);
    }

    public void Add(RecurringInvoiceSchedule schedule) => context.RecurringInvoiceSchedules.Add(schedule);

    public async Task<IReadOnlyList<RecurringInvoiceSchedule>> DueAsync(DateOnly today, CancellationToken ct)
        => await context.RecurringInvoiceSchedules.IgnoreQueryFilters()
            .Where(x => x.IsActive && x.NextRunOn <= today).OrderBy(x => x.NextRunOn)
            .Take(100).ToListAsync(ct);
}
