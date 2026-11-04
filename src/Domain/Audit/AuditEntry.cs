using Domain.Common;

namespace Domain.Audit;

// Append-only record of who changed what. The database context refuses to modify or delete these.
public sealed class AuditEntry : BaseEntity, ITenantOwned
{
    private AuditEntry() { }

    public Guid OrganizationId { get; private set; }
    public Guid? UserId { get; private set; }        // empty for unauthenticated actions such as registration
    public string Action { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    public string? Details { get; private set; }

    public static AuditEntry Create(
        Guid organizationId, Guid? userId, string action, string entityType, Guid entityId, string? details)
    {
        if (organizationId == Guid.Empty) throw new DomainException("OrganizationId is required.");

        var text = details is { Length: > 1000 } ? details[..1000] : details;

        return new AuditEntry
        {
            OrganizationId = organizationId,
            UserId = userId,
            Action = Guard.NotBlank(action, nameof(Action), 100),
            EntityType = Guard.NotBlank(entityType, nameof(EntityType), 50),
            EntityId = entityId,
            Details = Guard.OptionalText(text, nameof(Details), 1000)
        };
    }
}