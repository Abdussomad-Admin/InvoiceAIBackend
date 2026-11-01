using Domain.Users;

namespace Application.Auth;

public sealed record VerifyEmailCommand(string Token);
public sealed record ResendVerificationCommand(string Email);
public sealed record ForgotPasswordCommand(string Email);
public sealed record ResetPasswordCommand(string Token, string NewPassword);

public interface IUserTokenRepository
{
    Task<UserToken?> GetByHashAsync(UserTokenPurpose purpose, string tokenHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserToken>> GetUnusedAsync(Guid userId, UserTokenPurpose purpose, CancellationToken cancellationToken = default);
    void Add(UserToken token);
}

public static class PasswordPolicy
{
    public static void Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new Domain.Common.DomainException("Password must be at least 8 characters.");
        if (password.Length > 128)
            throw new Domain.Common.DomainException("Password must be at most 128 characters.");
    }
}