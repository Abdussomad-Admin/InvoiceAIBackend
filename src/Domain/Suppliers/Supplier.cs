using Domain.Common;

namespace Domain.Suppliers;

public sealed class Supplier : BaseEntity, ITenantOwned
{
    private Supplier() { }

    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? TaxIdentificationNumber { get; private set; }
    public string? AddressLine1 { get; private set; }
    public string? AddressLine2 { get; private set; }
    public string? City { get; private set; }
    public string? Region { get; private set; }
    public string? PostalCode { get; private set; }
    public string? CountryCode { get; private set; }
    public string? Notes { get; private set; }
    public int DefaultPaymentTermsDays { get; private set; } = 30;
    public bool IsArchived { get; private set; }

    public static Supplier Create(Guid organizationId, string name)
    {
        if (organizationId == Guid.Empty)
            throw new DomainException("OrganizationId is required.");

        return new Supplier
        {
            OrganizationId = organizationId,
            Name = Guard.NotBlank(name, nameof(Name), 200)
        };
    }

    public void Update(
        string name, string? email, string? phone, string? taxIdentificationNumber,
        string? addressLine1, string? addressLine2, string? city, string? region,
        string? postalCode, string? countryCode, string? notes, int defaultPaymentTermsDays)
    {
        if (defaultPaymentTermsDays is < 0 or > 365)
            throw new DomainException("Payment terms must be between 0 and 365 days.");

        Name = Guard.NotBlank(name, nameof(Name), 200);
        Email = ValidEmail(email);
        Phone = Guard.OptionalText(phone, nameof(Phone), 30);
        TaxIdentificationNumber = Guard.OptionalText(taxIdentificationNumber, nameof(TaxIdentificationNumber), 50);
        AddressLine1 = Guard.OptionalText(addressLine1, nameof(AddressLine1), 200);
        AddressLine2 = Guard.OptionalText(addressLine2, nameof(AddressLine2), 200);
        City = Guard.OptionalText(city, nameof(City), 100);
        Region = Guard.OptionalText(region, nameof(Region), 100);
        PostalCode = Guard.OptionalText(postalCode, nameof(PostalCode), 20);
        CountryCode = string.IsNullOrWhiteSpace(countryCode) ? null : Guard.IsoCode(countryCode, 2, nameof(CountryCode));
        Notes = Guard.OptionalText(notes, nameof(Notes), 2000);
        DefaultPaymentTermsDays = defaultPaymentTermsDays;
    }

    public void Archive() => IsArchived = true;
    public void Restore() => IsArchived = false;

    private static string? ValidEmail(string? email)
    {
        var value = Guard.OptionalText(email, nameof(Email), 254)?.ToLowerInvariant();
        if (value is not null && (value.IndexOf('@') < 1 || value.EndsWith('@')))
            throw new DomainException("Supplier email is not valid.");
        return value;
    }
}