using Domain.Common;

namespace Domain.Users
{
    public enum UserTokenPurpose
    {
        EmailVerification = 0,
        PasswordReset = 1
    }

    public sealed class UserToken : BaseEntity
    {
        private UserToken() { }

        public Guid UserId { get; private set; }
        public UserTokenPurpose Purpose { get; private set; }
        public string TokenHash { get; private set; } = null!;
        public DateTimeOffset ExpiresAt { get; private set; }
        public DateTimeOffset? UsedAt { get; private set; }

        public static UserToken Create(
            Guid userId, UserTokenPurpose purpose, string tokenHash, DateTimeOffset expiresAt)
        {
            if (userId == Guid.Empty) throw new DomainException("UserId is required.");
            if (!Enum.IsDefined(purpose)) throw new DomainException("Token purpose is not valid.");

            return new UserToken
            {
                UserId = userId,
                Purpose = purpose,
                TokenHash = Guard.NotBlank(tokenHash, nameof(TokenHash), 128),
                ExpiresAt = expiresAt
            };
        }

        public bool IsUsable(DateTimeOffset now) => UsedAt is null && now < ExpiresAt;

        public void MarkUsed(DateTimeOffset now) => UsedAt ??= now;
    }
}