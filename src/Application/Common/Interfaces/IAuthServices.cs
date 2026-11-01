namespace Application.Common.Interfaces;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public sealed record TokenResult(string Token, DateTimeOffset ExpiresAt);

public interface IJwtTokenService
{
    TimeSpan RefreshLifetime { get; }
    TokenResult Create(Guid userId, string email, Guid? organizationId);
}