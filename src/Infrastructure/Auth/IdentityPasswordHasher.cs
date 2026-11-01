using Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Auth;

public sealed class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public bool Verify(string hash, string password)
        => _hasher.VerifyHashedPassword(null!, hash, password) != PasswordVerificationResult.Failed;
}