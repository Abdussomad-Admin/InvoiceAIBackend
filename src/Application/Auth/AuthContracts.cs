using Domain.Organizations;

namespace Application.Auth
{
    public sealed record RegisterCommand(
        string Email, string Password, string FullName, string OrganizationName,
        string CountryCode = "NG", string CurrencyCode = "NGN", string TimeZoneId = "Africa/Lagos",
        BusinessAccountType AccountType = BusinessAccountType.Organization);

    public sealed record RegisterResult(Guid UserId, Guid OrganizationId);

    public sealed record LoginCommand(string Email, string Password);

    public sealed record LoginResult(string Token, DateTimeOffset ExpiresAt, Guid? OrganizationId, string RefreshToken);
}
