using System.Security.Claims;
using System.Text;
using Application.Common.Interfaces;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Auth;

public sealed class JwtTokenService(JwtSettings settings, TimeProvider clock) : IJwtTokenService
{
    public TimeSpan RefreshLifetime => TimeSpan.FromDays(settings.RefreshTokenDays);

    public TokenResult Create(Guid userId, string email, Guid? organizationId)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(settings.ExpiryMinutes);

        var claims = new List<Claim> { new("sub", userId.ToString()), new("email", email) };
        if (organizationId.HasValue)
            claims.Add(new Claim("org_id", organizationId.Value.ToString()));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
                SecurityAlgorithms.HmacSha256)
        };

        return new TokenResult(new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}