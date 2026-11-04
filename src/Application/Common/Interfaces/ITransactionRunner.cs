namespace Application.Common.Interfaces;

public interface ITransactionRunner
{
    Task RunInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default);
}