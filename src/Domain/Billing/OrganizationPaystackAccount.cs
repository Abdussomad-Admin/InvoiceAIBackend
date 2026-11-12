using Domain.Common;

namespace Domain.Billing;

public sealed class OrganizationPaystackAccount : BaseEntity, ITenantOwned
{
    private OrganizationPaystackAccount() { }

    public Guid OrganizationId { get; private set; }
    public string SubaccountCode { get; private set; } = null!;
    public string CurrencyCode { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public bool IsVerified { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }

    public static OrganizationPaystackAccount Create(Guid organizationId, string code, string currencyCode)
    {
        if (organizationId == Guid.Empty) throw new DomainException("Organization is required.");
        return new OrganizationPaystackAccount
        {
            OrganizationId = organizationId,
            SubaccountCode = Guard.NotBlank(code, nameof(code), 100),
            CurrencyCode = Guard.IsoCode(currencyCode, 3, nameof(currencyCode))
        };
    }

    public void RefreshProviderStatus(bool active, bool verified, DateTimeOffset checkedAt)
    {
        IsActive = active;
        IsVerified = verified;
        LastCheckedAt = checkedAt;
    }
}
