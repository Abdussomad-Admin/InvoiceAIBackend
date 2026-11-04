namespace Infrastructure.Persistence;

public sealed class InvoiceCounter
{
    public Guid OrganizationId { get; set; }
    public int LastNumber { get; set; }
}