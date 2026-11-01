using Domain.Organizations;

namespace Application.Organizations;

public sealed record InviteMemberCommand(string Email, OrganizationRole Role);
public sealed record AcceptInvitationCommand(string Token, string? FullName, string? Password);
public sealed record ChangeMemberRoleCommand(OrganizationRole Role);

public sealed record AcceptInvitationResult(Guid UserId, Guid OrganizationId);

public sealed record MemberDto(
    Guid UserId, string Email, string FullName, OrganizationRole Role, bool IsActive, DateTimeOffset JoinedAt);

public sealed record InvitationDto(
    Guid Id, string Email, OrganizationRole Role, DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt);

public interface IInvitationRepository
{
    Task<Invitation?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<Invitation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Invitation>> GetPendingByEmailAsync(string email, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Invitation>> ListPendingAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    void Add(Invitation invitation);
}