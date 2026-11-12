using Application.Common;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Domain.Organizations;

namespace Application.Organizations;

public sealed record OrganizationDto(
    Guid Id, string Name, BusinessAccountType AccountType, bool IsOnboardingComplete, string CountryCode, string BaseCurrencyCode, string TimeZoneId,
    string? Email, string? Phone, string? TaxIdentificationNumber,
    string? AddressLine1, string? AddressLine2, string? City, string? Region, string? PostalCode,
    int DefaultPaymentTermsDays);

public sealed record UpdateOrganizationInput(
    string Name, string? Email, string? Phone, string? TaxIdentificationNumber,
    string? AddressLine1, string? AddressLine2, string? City, string? Region, string? PostalCode,
    int DefaultPaymentTermsDays);

internal static class OrganizationMapping
{
    public static OrganizationDto ToDto(this Organization o) => new(
        o.Id, o.Name, o.AccountType, o.IsOnboardingComplete, o.CountryCode, o.BaseCurrencyCode, o.TimeZoneId,
        o.Email, o.Phone, o.TaxIdentificationNumber,
        o.AddressLine1, o.AddressLine2, o.City, o.Region, o.PostalCode,
        o.DefaultPaymentTermsDays);
}

public sealed class CompleteOrganizationOnboardingHandler(
    IOrganizationRepository organizations, IUnitOfWork unitOfWork, ICurrentOrganization currentOrganization)
{
    public async Task Handle(CancellationToken cancellationToken)
    {
        var organization = await organizations.GetByIdAsync(currentOrganization.RequireId(), cancellationToken)
            ?? throw new NotFoundException("Organization not found.");
        organization.CompleteOnboarding();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class GetOrganizationHandler(
    IOrganizationRepository organizations, ICurrentOrganization currentOrganization)
{
    public async Task<OrganizationDto> Handle(CancellationToken cancellationToken)
    {
        var organization = await organizations.GetByIdAsync(currentOrganization.RequireId(), cancellationToken)
            ?? throw new NotFoundException("Organization not found.");
        return organization.ToDto();
    }
}

public sealed class UpdateOrganizationHandler(
    IOrganizationRepository organizations, IUnitOfWork unitOfWork, ICurrentOrganization currentOrganization)
{
    public async Task<OrganizationDto> Handle(UpdateOrganizationInput input, CancellationToken cancellationToken)
    {
        // Always the current organization from the token: the caller cannot name another one.
        var organization = await organizations.GetByIdAsync(currentOrganization.RequireId(), cancellationToken)
            ?? throw new NotFoundException("Organization not found.");

        organization.UpdateProfile(
            input.Name, input.Email, input.Phone, input.TaxIdentificationNumber,
            input.AddressLine1, input.AddressLine2, input.City, input.Region, input.PostalCode);
        organization.SetDefaultPaymentTerms(input.DefaultPaymentTermsDays);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return organization.ToDto();
    }
}
