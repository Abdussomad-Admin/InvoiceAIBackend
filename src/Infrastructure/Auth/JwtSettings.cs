namespace Infrastructure.Auth;

public sealed class JwtSettings
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string Key { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}