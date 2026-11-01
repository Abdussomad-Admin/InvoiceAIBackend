using Domain.Common;
using Domain.Users;

namespace Domain.Organizations;

public sealed class Invitation : BaseEntity, ITenantOwned
{
    private Invitation() { }

    public Guid OrganizationId { get; private set; }
    public string Email { get; private set; } = null!;
    public OrganizationRole Role { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public Guid InvitedByUserId { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static Invitation Create(
        Guid organizationId, string email, OrganizationRole role,
        string tokenHash, Guid invitedByUserId, DateTimeOffset expiresAt)
    {
        if (organizationId == Guid.Empty) throw new DomainException("OrganizationId is required.");
        if (invitedByUserId == Guid.Empty) throw new DomainException("InvitedByUserId is required.");
        if (!Enum.IsDefined(role)) throw new DomainException("Role is not valid.");

        return new Invitation
        {
            OrganizationId = organizationId,
            Email = User.NormalizeEmail(email),
            Role = role,
            TokenHash = Guard.NotBlank(tokenHash, nameof(TokenHash), 128),
            InvitedByUserId = invitedByUserId,
            ExpiresAt = expiresAt
        };
    }

    public bool IsPending(DateTimeOffset now) => AcceptedAt is null && RevokedAt is null && now < ExpiresAt;

    public void MarkAccepted(DateTimeOffset now) => AcceptedAt ??= now;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}