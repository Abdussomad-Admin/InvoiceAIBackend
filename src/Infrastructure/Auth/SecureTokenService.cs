using System.Security.Cryptography;
using System.Text;
using Application.Common.Interfaces;

namespace Infrastructure.Auth;

public sealed class SecureTokenService : ISecureTokenService
{
    public string Generate()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}