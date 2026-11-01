using Domain.Common;

namespace Domain.Users;

public sealed class User : BaseEntity
{
    private User() { }

    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public DateTimeOffset? EmailVerifiedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public bool IsActive { get; private set; } = true;

    public bool IsEmailVerified => EmailVerifiedAt.HasValue;

    public static string NormalizeEmail(string? email)
    {
        var value = Guard.NotBlank(email, nameof(Email), 254).ToLowerInvariant();

        var at = value.IndexOf('@');
        if (at < 1 || at != value.LastIndexOf('@') || at == value.Length - 1)
            throw new DomainException("Email address is not valid.");

        return value;
    }

    public static User Register(string email, string passwordHash, string fullName)
    {
        return new User
        {
            Email = NormalizeEmail(email),
            PasswordHash = Guard.NotBlank(passwordHash, nameof(PasswordHash), 500),
            FullName = Guard.NotBlank(fullName, nameof(FullName), 200)
        };
    }

    public void VerifyEmail(DateTimeOffset now) => EmailVerifiedAt ??= now;

    public void RecordLogin(DateTimeOffset now) => LastLoginAt = now;

    public void ChangePasswordHash(string newHash)
        => PasswordHash = Guard.NotBlank(newHash, nameof(PasswordHash), 500);

    public void UpdateName(string fullName)
        => FullName = Guard.NotBlank(fullName, nameof(FullName), 200);

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}