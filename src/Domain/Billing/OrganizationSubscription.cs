using Domain.Common;

namespace Domain.Billing;

public enum SubscriptionStatus { Pending, Active, PastDue, NonRenewing, Canceled }

public sealed class OrganizationSubscription : BaseEntity, ITenantOwned
{
    private OrganizationSubscription() { }

    public Guid OrganizationId { get; private set; }
    public string PlanCode { get; private set; } = null!;
    public string CurrencyCode { get; private set; } = null!;
    public long AmountMinor { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public string? PaystackCustomerCode { get; private set; }
    public string? PaystackSubscriptionCode { get; private set; }
    public DateTimeOffset? CurrentPeriodStart { get; private set; }
    public DateTimeOffset? CurrentPeriodEnd { get; private set; }
    public DateTimeOffset? LastPaidAt { get; private set; }

    public static OrganizationSubscription Start(Guid organizationId, string planCode, string currencyCode, long amountMinor)
        => new() { OrganizationId = organizationId, PlanCode = planCode, CurrencyCode = currencyCode,
            AmountMinor = amountMinor, Status = SubscriptionStatus.Pending };

    public void Activate(string? customerCode, string? subscriptionCode, DateTimeOffset now, DateTimeOffset? periodEnd = null)
    {
        Status = SubscriptionStatus.Active;
        PaystackCustomerCode = customerCode ?? PaystackCustomerCode;
        PaystackSubscriptionCode = subscriptionCode ?? PaystackSubscriptionCode;
        CurrentPeriodStart = now;
        CurrentPeriodEnd = periodEnd;
        LastPaidAt = now;
    }

    public void SetProviderDetails(string? customerCode, string? subscriptionCode)
    {
        PaystackCustomerCode = customerCode ?? PaystackCustomerCode;
        PaystackSubscriptionCode = subscriptionCode ?? PaystackSubscriptionCode;
    }

    public void SetStatus(SubscriptionStatus status, DateTimeOffset? periodEnd = null)
    {
        Status = status;
        if (periodEnd.HasValue) CurrentPeriodEnd = periodEnd;
    }
}
