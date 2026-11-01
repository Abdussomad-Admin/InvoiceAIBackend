namespace Domain.Common;

public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public void MarkCreated(DateTimeOffset now) => CreatedAt = now;
    public void MarkUpdated(DateTimeOffset now) => UpdatedAt = now;
}