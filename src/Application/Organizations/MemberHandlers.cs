using Application.Auth;
using Application.Common;
using Application.Billing;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Users;
using Domain.Common;
using Domain.Organizations;
using Domain.Users;

namespace Application.Organizations;

public sealed class InviteMemberHandler(
    IInvitationRepository invitations,
    IMembershipRepository memberships,
    IOrganizationRepository organizations,
    IUserRepository users,
    ISecureTokenService secure,
    IEmailSender emailSender,
    IUnitOfWork unitOfWork,
    ICurrentOrganization currentOrganization,
    ICurrentUser currentUser,
    AppLinkSettings links,
    TimeProvider clock,
    IPlanLimitChecker? planLimits = null)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public async Task<InvitationDto> Handle(InviteMemberCommand command, CancellationToken cancellationToken)
    {
        var organizationId = currentOrganization.RequireId();
        var actorId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");
        var actor = await memberships.GetAsync(actorId, organizationId, cancellationToken)
            ?? throw new ForbiddenException("You are not a member of this organization.");

        if (!Enum.IsDefined(command.Role)) throw new DomainException("Role is not valid.");
        if (!MemberRules.CanAssign(actor.Role, command.Role))
            throw new ForbiddenException("You are not allowed to grant that role.");

        var organization = await organizations.GetByIdAsync(organizationId, cancellationToken)
            ?? throw new NotFoundException("Organization not found.");
        if (organization.AccountType == BusinessAccountType.Freelancer)
            throw new DomainException("Freelancer workspaces are for individual use and cannot invite teammates.");

        var email = User.NormalizeEmail(command.Email);

        var existingUser = await users.GetByEmailAsync(email, cancellationToken);
        if (existingUser is not null)
        {
            var existing = await memberships.GetAsync(existingUser.Id, organizationId, cancellationToken);
            if (existing is { IsActive: true })
                throw new ConflictException("This person is already a member.");
        }

        if (planLimits is not null) await planLimits.EnsureMemberCapacityAsync(email, cancellationToken);

        var now = clock.GetUtcNow();

        // Only the newest invitation for this address should work.
        foreach (var old in await invitations.GetPendingByEmailAsync(email, now, cancellationToken))
            old.Revoke(now);

        var inviter = await users.GetByIdAsync(actorId, cancellationToken);

        var rawToken = secure.Generate();
        var invitation = Invitation.Create(
            organizationId, email, command.Role, secure.Hash(rawToken), actorId, now.Add(Lifetime));
        invitations.Add(invitation);

        // Save first, then send, so nobody receives a link for an invitation that failed to save.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var link = $"{links.FrontendBaseUrl.TrimEnd('/')}/accept-invitation?token={rawToken}";
        var body =
            $"Hello,\n\n{inviter?.FullName ?? "A colleague"} invited you to join {organization.Name} " +
            $"on Invoice AI as {command.Role}.\n\nAccept the invitation:\n{link}\n\n" +
            "This invitation expires in 7 days.\n";

        await emailSender.SendAsync(
            new OutgoingEmail(email, $"You are invited to join {organization.Name} on Invoice AI", body),
            cancellationToken);

        return new InvitationDto(invitation.Id, invitation.Email, invitation.Role, invitation.ExpiresAt, invitation.CreatedAt);
    }
}

public sealed class AcceptInvitationHandler(
    IInvitationRepository invitations,
    IUserRepository users,
    IMembershipRepository memberships,
    ISecureTokenService secure,
    IPasswordHasher passwordHasher,
    ITenantWriteBypass tenantBypass,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<AcceptInvitationResult> Handle(AcceptInvitationCommand command, CancellationToken cancellationToken)
    {
        const string invalid = "This invitation is invalid or has expired.";
        var now = clock.GetUtcNow();

        var invitation = await invitations.GetByHashAsync(secure.Hash(command.Token ?? ""), cancellationToken);
        if (invitation is null || !invitation.IsPending(now)) throw new DomainException(invalid);

        var user = await users.GetByEmailAsync(invitation.Email, cancellationToken);
        if (user is null)
        {
            // New person: they choose a name and password. Using the emailed link proves they own the address.
            PasswordPolicy.Validate(command.Password);
            user = User.Register(invitation.Email, passwordHasher.Hash(command.Password!), command.FullName ?? "");
            user.VerifyEmail(now);
            users.Add(user);
        }
        else if (!user.IsActive)
        {
            throw new DomainException(invalid);
        }

        var existing = await memberships.GetAnyTenantAsync(user.Id, invitation.OrganizationId, cancellationToken);
        if (existing is { IsActive: true })
            throw new ConflictException("You are already a member of this organization.");

        if (existing is null)
        {
            memberships.Add(Membership.Create(user.Id, invitation.OrganizationId, invitation.Role));
        }
        else
        {
            // A previously deactivated member rejoining.
            existing.ChangeRole(invitation.Role);
            existing.Activate();
        }

        invitation.MarkAccepted(now);

        // The caller may be signed into a different organization, so this one save is exempt from the write guard.
        using (tenantBypass.Allow())
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new AcceptInvitationResult(user.Id, invitation.OrganizationId);
    }
}

public sealed class ListMembersHandler(IMembershipRepository memberships)
{
    public Task<IReadOnlyList<MemberDto>> Handle(CancellationToken cancellationToken)
        => memberships.ListMembersAsync(cancellationToken);
}

public sealed class ListInvitationsHandler(IInvitationRepository invitations, TimeProvider clock)
{
    public async Task<IReadOnlyList<InvitationDto>> Handle(CancellationToken cancellationToken)
    {
        var pending = await invitations.ListPendingAsync(clock.GetUtcNow(), cancellationToken);
        return pending
            .Select(i => new InvitationDto(i.Id, i.Email, i.Role, i.ExpiresAt, i.CreatedAt))
            .ToList();
    }
}

public sealed class RevokeInvitationHandler(
    IInvitationRepository invitations, IMembershipRepository memberships,
    IUnitOfWork unitOfWork, ICurrentOrganization currentOrganization,
    ICurrentUser currentUser, TimeProvider clock)
{
    public async Task Handle(Guid invitationId, CancellationToken cancellationToken)
    {
        var organizationId = currentOrganization.RequireId();
        var actorId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");
        var actor = await memberships.GetAsync(actorId, organizationId, cancellationToken)
            ?? throw new ForbiddenException("You are not a member of this organization.");

        var invitation = await invitations.GetByIdAsync(invitationId, cancellationToken)
            ?? throw new NotFoundException("Invitation not found.");

        if (!MemberRules.CanAssign(actor.Role, invitation.Role))
            throw new ForbiddenException("You are not allowed to revoke that invitation.");

        invitation.Revoke(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class ChangeMemberRoleHandler(
    IMembershipRepository memberships, IUnitOfWork unitOfWork,
    ICurrentOrganization currentOrganization, ICurrentUser currentUser)
{
    public async Task Handle(Guid userId, ChangeMemberRoleCommand command, CancellationToken cancellationToken)
    {
        var organizationId = currentOrganization.RequireId();
        var actorId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");
        var actor = await memberships.GetAsync(actorId, organizationId, cancellationToken)
            ?? throw new ForbiddenException("You are not a member of this organization.");
        var target = await memberships.GetAsync(userId, organizationId, cancellationToken)
            ?? throw new NotFoundException("Member not found.");

        if (!Enum.IsDefined(command.Role)) throw new DomainException("Role is not valid.");
        if (!target.IsActive) throw new DomainException("This member is deactivated. Reactivate them first.");

        if (!MemberRules.CanManage(actor.Role, target.Role) || !MemberRules.CanAssign(actor.Role, command.Role))
            throw new ForbiddenException("You are not allowed to make that change.");

        if (target.Role == OrganizationRole.Owner && command.Role != OrganizationRole.Owner &&
            await memberships.CountActiveOwnersAsync(organizationId, cancellationToken) <= 1)
            throw new DomainException("An organization must keep at least one Owner.");

        target.ChangeRole(command.Role);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class SetMemberActiveHandler(
    IMembershipRepository memberships, IUnitOfWork unitOfWork,
    ICurrentOrganization currentOrganization, ICurrentUser currentUser)
{
    public async Task Handle(Guid userId, bool active, CancellationToken cancellationToken)
    {
        var organizationId = currentOrganization.RequireId();
        var actorId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");
        var actor = await memberships.GetAsync(actorId, organizationId, cancellationToken)
            ?? throw new ForbiddenException("You are not a member of this organization.");
        var target = await memberships.GetAsync(userId, organizationId, cancellationToken)
            ?? throw new NotFoundException("Member not found.");

        if (!MemberRules.CanManage(actor.Role, target.Role))
            throw new ForbiddenException("You are not allowed to change this member.");

        if (active)
        {
            target.Activate();
        }
        else
        {
            if (userId == actorId)
                throw new DomainException("You cannot deactivate your own membership.");

            if (target.IsActive && target.Role == OrganizationRole.Owner &&
                await memberships.CountActiveOwnersAsync(organizationId, cancellationToken) <= 1)
                throw new DomainException("An organization must keep at least one Owner.");

            target.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
