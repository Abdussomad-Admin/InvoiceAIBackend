using Domain.Common;

namespace Domain.Billing;

public enum BillingCheckoutStatus { Pending, Succeeded, Failed }

public sealed class BillingCheckout : BaseEntity, ITenantOwned
{
    private BillingCheckout() { }

    public Guid OrganizationId { get; private set; }
    public string PlanCode { get; private set; } = null!;
    public string CurrencyCode { get; private set; } = null!;
    public long AmountMinor { get; private set; }
    public string Reference { get; private set; } = null!;
    public BillingCheckoutStatus Status { get; private set; }
    public string? AuthorizationUrl { get; private set; }

    public static BillingCheckout Create(Guid organizationId, string planCode, string currencyCode, long amountMinor, string reference)
        => new() { OrganizationId = organizationId, PlanCode = planCode, CurrencyCode = currencyCode,
            AmountMinor = amountMinor, Reference = reference, Status = BillingCheckoutStatus.Pending };
    public void SetAuthorizationUrl(string value) => AuthorizationUrl = value;
    public void MarkSucceeded() => Status = BillingCheckoutStatus.Succeeded;
    public void MarkFailed() => Status = BillingCheckoutStatus.Failed;
}
