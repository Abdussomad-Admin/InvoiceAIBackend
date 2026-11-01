namespace Domain.Common;

public interface ITenantOwned
{
    Guid OrganizationId { get; }
}