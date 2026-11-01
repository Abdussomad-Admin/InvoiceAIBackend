using Domain.Common;

namespace Domain.Users
{
    public sealed class RefreshToken : BaseEntity
    {
        private RefreshToken() { }

        public Guid UserId { get; private set; }
        public Guid? OrganizationId { get; private set; }
        public string TokenHash { get; private set; } = null!;
        public DateTimeOffset ExpiresAt { get; private set; }
        public DateTimeOffset? RevokedAt { get; private set; }

        public static RefreshToken Create(
            Guid userId, Guid? organizationId, string tokenHash, DateTimeOffset expiresAt)
        {
            if (userId == Guid.Empty) throw new DomainException("UserId is required.");

            return new RefreshToken
            {
                UserId = userId,
                OrganizationId = organizationId,
                TokenHash = Guard.NotBlank(tokenHash, nameof(TokenHash), 128),
                ExpiresAt = expiresAt
            };
        }

        public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

        public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
    }
}