using Domain.Common;

namespace Domain.Organizations;

public sealed class Organization : BaseEntity
{
    private Organization() { }

    public string Name { get; private set; } = null!;
    public BusinessAccountType AccountType { get; private set; } = BusinessAccountType.Organization;
    public bool IsOnboardingComplete { get; private set; }
    public string CountryCode { get; private set; } = null!;
    public string BaseCurrencyCode { get; private set; } = null!;
    public string TimeZoneId { get; private set; } = null!;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? TaxIdentificationNumber { get; private set; }
    public string? AddressLine1 { get; private set; }
    public string? AddressLine2 { get; private set; }
    public string? City { get; private set; }
    public string? Region { get; private set; }
    public string? PostalCode { get; private set; }
    public string? LogoStorageKey { get; private set; }

    public int DefaultPaymentTermsDays { get; private set; } = 30;
    public bool IsArchived { get; private set; }

    public static Organization Create(
        string name, string countryCode, string baseCurrencyCode, string timeZoneId,
        BusinessAccountType accountType = BusinessAccountType.Organization)
    {
        return new Organization
        {
            Name = Guard.NotBlank(name, nameof(Name), 200),
            AccountType = accountType,
            CountryCode = Guard.IsoCode(countryCode, 2, nameof(CountryCode)),
            BaseCurrencyCode = Guard.IsoCode(baseCurrencyCode, 3, nameof(BaseCurrencyCode)),
            TimeZoneId = Guard.TimeZone(timeZoneId, nameof(TimeZoneId))
        };
    }

    public void UpdateProfile(
        string name, string? email, string? phone, string? taxIdentificationNumber,
        string? addressLine1, string? addressLine2, string? city, string? region, string? postalCode)
    {
        Name = Guard.NotBlank(name, nameof(Name), 200);
        Email = ValidEmail(email);
        Phone = Guard.OptionalText(phone, nameof(Phone), 30);
        TaxIdentificationNumber = Guard.OptionalText(taxIdentificationNumber, nameof(TaxIdentificationNumber), 50);
        AddressLine1 = Guard.OptionalText(addressLine1, nameof(AddressLine1), 200);
        AddressLine2 = Guard.OptionalText(addressLine2, nameof(AddressLine2), 200);
        City = Guard.OptionalText(city, nameof(City), 100);
        Region = Guard.OptionalText(region, nameof(Region), 100);
        PostalCode = Guard.OptionalText(postalCode, nameof(PostalCode), 20);
    }

    public void SetDefaultPaymentTerms(int days)
    {
        if (days is < 0 or > 365)
            throw new DomainException("Payment terms must be between 0 and 365 days.");

        DefaultPaymentTermsDays = days;
    }

    public void CompleteOnboarding() => IsOnboardingComplete = true;

    private static string? ValidEmail(string? email)
    {
        var value = Guard.OptionalText(email, nameof(Email), 254)?.ToLowerInvariant();
        if (value is not null && (value.IndexOf('@') < 1 || value.EndsWith('@')))
            throw new DomainException("Organization email is not valid.");
        return value;
    }

    public void SetLogo(string? storageKey)
        => LogoStorageKey = Guard.OptionalText(storageKey, nameof(LogoStorageKey), 500);

    public void Archive() => IsArchived = true;
}
