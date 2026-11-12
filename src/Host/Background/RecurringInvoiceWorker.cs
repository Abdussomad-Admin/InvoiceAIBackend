using Application.Common.Interfaces;
using Application.Invoices;
using Domain.Invoices;
using Host;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Host.Background;

public sealed class RecurringInvoiceWorker(IServiceScopeFactory scopes, ILogger<RecurringInvoiceWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Recurring invoice generation failed."); }
            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var schedules = scope.ServiceProvider.GetRequiredService<IRecurringInvoiceRepository>();
        var runner = scope.ServiceProvider.GetRequiredService<ITransactionRunner>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var invoices = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();
        var due = await schedules.DueAsync(DateOnly.FromDateTime(DateTime.UtcNow), ct);
        foreach (var schedule in due)
        {
            await runner.RunInTransactionAsync(async () =>
            {
                // Recheck after acquiring the transaction so another worker instance cannot duplicate a run.
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM \"RecurringInvoiceSchedules\" WHERE \"Id\" = {schedule.Id} FOR UPDATE", ct);
                var locked = await db.RecurringInvoiceSchedules.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.Id == schedule.Id, ct);
                if (locked is null || !locked.IsActive || locked.NextRunOn > DateOnly.FromDateTime(DateTime.UtcNow)) return;

                var customerIsActive = await db.Customers.IgnoreQueryFilters()
                    .AnyAsync(c => c.Id == locked.CustomerId && c.OrganizationId == locked.OrganizationId && !c.IsArchived, ct);
                if (!customerIsActive)
                {
                    locked.Pause();
                    logger.LogWarning("Paused recurring invoice schedule {ScheduleId}: its customer is missing or archived.", locked.Id);
                    await unitOfWork.SaveChangesAsync(ct);
                    return;
                }

                var maxCatchUp = 12;
                while (locked.IsActive && locked.NextRunOn <= DateOnly.FromDateTime(DateTime.UtcNow) && maxCatchUp-- > 0)
                {
                    var invoice = Invoice.CreateDraft(locked.OrganizationId, locked.CustomerId,
                        locked.CurrencyCode, locked.PaymentTermsDays, $"Recurring: {locked.Description}",
                        locked.BaseCurrencyCode, locked.ExchangeRateToBase);
                    invoice.ReplaceLines(locked.Lines());
                    invoices.Add(invoice);
                    locked.Advance();
                }
                await unitOfWork.SaveChangesAsync(ct);
            }, ct);
        }
    }
}
