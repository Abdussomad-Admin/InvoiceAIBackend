namespace Application.Billing;

public interface IPlanLimitChecker
{
    Task EnsureInvoiceCapacityAsync(CancellationToken cancellationToken = default);
    Task EnsureMemberCapacityAsync(string email, CancellationToken cancellationToken = default);
    Task EnsureAiExtractionCapacityAsync(CancellationToken cancellationToken = default);
    Task EnsureStorageCapacityAsync(long bytesToAdd, long serverLimitBytes, CancellationToken cancellationToken = default);
}
