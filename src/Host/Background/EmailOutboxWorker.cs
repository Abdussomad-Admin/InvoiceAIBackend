namespace Host.Background;

public sealed class EmailOutboxWorker(
    IServiceScopeFactory scopeFactory, ReminderSettings settings, ILogger<EmailOutboxWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>()
                    .RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Email outbox worker cycle failed");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(settings.PollIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }
}
