using Application.Auth;
using Application.Common;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Organizations;
using Domain.Common;
using Domain.Organizations;
using Infrastructure.Auth;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public sealed class TestCurrentUser(Guid? userId) : ICurrentUser
{
    public Guid? UserId => userId;
}

public class MemberRulesTests
{
    [Fact]
    public void Owner_CanAssignAndManageAnyRole()
    {
        foreach (var role in Enum.GetValues<OrganizationRole>())
        {
            Assert.True(MemberRules.CanAssign(OrganizationRole.Owner, role));
            Assert.True(MemberRules.CanManage(OrganizationRole.Owner, role));
        }
    }

    [Fact]
    public void Admin_CannotAssignOrManageOwner_ButCanHandleOthers()
    {
        Assert.False(MemberRules.CanAssign(OrganizationRole.Admin, OrganizationRole.Owner));
        Assert.False(MemberRules.CanManage(OrganizationRole.Admin, OrganizationRole.Owner));
        Assert.True(MemberRules.CanAssign(OrganizationRole.Admin, OrganizationRole.Accountant));
        Assert.True(MemberRules.CanManage(OrganizationRole.Admin, OrganizationRole.Staff));
    }

    [Fact]
    public void StaffAndAccountant_CannotAssignOrManageAnyone()
    {
        foreach (var actor in new[] { OrganizationRole.Staff, OrganizationRole.Accountant })
            foreach (var target in Enum.GetValues<OrganizationRole>())
            {
                Assert.False(MemberRules.CanAssign(actor, target));
                Assert.False(MemberRules.CanManage(actor, target));
            }
    }
}

public class MemberTests
{
    private static readonly FixedTimeProvider Clock =
        new(new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero));

    private static async Task<(string Email, RegisterResult Result)> RegisterOwnerAsync()
    {
        var email = $"{Guid.NewGuid()}@example.com";
        await using var ctx = TestDatabase.CreateContext(Clock);
        var handler = new RegisterHandler(
            new UserRepository(ctx), new OrganizationRepository(ctx), new MembershipRepository(ctx),
            ctx, new IdentityPasswordHasher(), AccountTestHelpers.CreateMailer(ctx, Clock),
            new CapturingEmailSender());
        var result = await handler.Handle(
            new RegisterCommand(email, "Passw0rd!23", "Owner Person", $"Acme {Guid.NewGuid()}"),
            CancellationToken.None);
        return (email, result);
    }

    private static async Task<string> InviteAsync(
        Guid actorId, Guid organizationId, string email, OrganizationRole role)
    {
        var sender = new CapturingEmailSender();
        await using var ctx = TestDatabase.CreateContext(Clock, organizationId);
        var handler = new InviteMemberHandler(
            new InvitationRepository(ctx), new MembershipRepository(ctx), new OrganizationRepository(ctx),
            new UserRepository(ctx), new SecureTokenService(), sender, ctx,
            new TestCurrentOrganization(organizationId), new TestCurrentUser(actorId),
            new AppLinkSettings("http://localhost:5173"), Clock);

        await handler.Handle(new InviteMemberCommand(email, role), CancellationToken.None);
        return sender.LastTokenFor(email.ToLowerInvariant());
    }

    private static async Task<AcceptInvitationResult> AcceptAsync(
        string token, string? fullName, string? password,
        Guid? callerOrganizationId = null, TimeProvider? clock = null)
    {
        clock ??= Clock;
        await using var ctx = TestDatabase.CreateContext(clock, callerOrganizationId);
        var handler = new AcceptInvitationHandler(
            new InvitationRepository(ctx), new UserRepository(ctx), new MembershipRepository(ctx),
            new SecureTokenService(), new IdentityPasswordHasher(), ctx, ctx, clock);
        return await handler.Handle(new AcceptInvitationCommand(token, fullName, password), CancellationToken.None);
    }

    private static async Task<AcceptInvitationResult> JoinAsync(RegisterResult owner, OrganizationRole role)
    {
        var token = await InviteAsync(
            owner.UserId, owner.OrganizationId, $"{Guid.NewGuid()}@example.com", role);
        return await AcceptAsync(token, "Team Member", "Passw0rd!23");
    }

    private static async Task ChangeRoleAsync(
        Guid actorId, Guid organizationId, Guid targetUserId, OrganizationRole role)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, organizationId);
        var handler = new ChangeMemberRoleHandler(
            new MembershipRepository(ctx), ctx,
            new TestCurrentOrganization(organizationId), new TestCurrentUser(actorId));
        await handler.Handle(targetUserId, new ChangeMemberRoleCommand(role), CancellationToken.None);
    }

    private static async Task SetActiveAsync(
        Guid actorId, Guid organizationId, Guid targetUserId, bool active)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, organizationId);
        var handler = new SetMemberActiveHandler(
            new MembershipRepository(ctx), ctx,
            new TestCurrentOrganization(organizationId), new TestCurrentUser(actorId));
        await handler.Handle(targetUserId, active, CancellationToken.None);
    }

    [Fact]
    public async Task AcceptingAnInvitation_CreatesAUserWithTheInvitedRole()
    {
        var (_, owner) = await RegisterOwnerAsync();

        var accepted = await JoinAsync(owner, OrganizationRole.Accountant);

        await using var ctx = TestDatabase.CreateContext(Clock, owner.OrganizationId);
        var membership = await ctx.Memberships.SingleAsync(m => m.UserId == accepted.UserId);
        Assert.Equal(OrganizationRole.Accountant, membership.Role);
        Assert.True(membership.IsActive);
    }

    [Fact]
    public async Task AnInvitationToken_WorksOnlyOnce()
    {
        var (_, owner) = await RegisterOwnerAsync();
        var token = await InviteAsync(
            owner.UserId, owner.OrganizationId, $"{Guid.NewGuid()}@example.com", OrganizationRole.Staff);

        await AcceptAsync(token, "Team Member", "Passw0rd!23");

        await Assert.ThrowsAsync<DomainException>(() => AcceptAsync(token, "Team Member", "Passw0rd!23"));
    }

    [Fact]
    public async Task AnExpiredInvitation_IsRejected()
    {
        var (_, owner) = await RegisterOwnerAsync();
        var token = await InviteAsync(
            owner.UserId, owner.OrganizationId, $"{Guid.NewGuid()}@example.com", OrganizationRole.Staff);
        var eightDaysLater = new FixedTimeProvider(Clock.GetUtcNow().AddDays(8));

        await Assert.ThrowsAsync<DomainException>(
            () => AcceptAsync(token, "Team Member", "Passw0rd!23", clock: eightDaysLater));
    }

    [Fact]
    public async Task AnAdmin_CannotInviteAnOwner()
    {
        var (_, owner) = await RegisterOwnerAsync();
        var admin = await JoinAsync(owner, OrganizationRole.Admin);

        await Assert.ThrowsAsync<ForbiddenException>(() => InviteAsync(
            admin.UserId, owner.OrganizationId, $"{Guid.NewGuid()}@example.com", OrganizationRole.Owner));
    }

    [Fact]
    public async Task StaffCannotInviteAnyone()
    {
        var (_, owner) = await RegisterOwnerAsync();
        var staff = await JoinAsync(owner, OrganizationRole.Staff);

        await Assert.ThrowsAsync<ForbiddenException>(() => InviteAsync(
            staff.UserId, owner.OrganizationId, $"{Guid.NewGuid()}@example.com", OrganizationRole.Staff));
    }

    [Fact]
    public async Task TheLastOwner_CannotBeDemoted()
    {
        var (_, owner) = await RegisterOwnerAsync();

        await Assert.ThrowsAsync<DomainException>(() => ChangeRoleAsync(
            owner.UserId, owner.OrganizationId, owner.UserId, OrganizationRole.Admin));
    }

    [Fact]
    public async Task AnOwner_CannotDeactivateThemselves()
    {
        var (_, owner) = await RegisterOwnerAsync();

        await Assert.ThrowsAsync<DomainException>(() => SetActiveAsync(
            owner.UserId, owner.OrganizationId, owner.UserId, false));
    }

    [Fact]
    public async Task AnAdmin_CannotDeactivateAnOwner()
    {
        var (_, owner) = await RegisterOwnerAsync();
        var admin = await JoinAsync(owner, OrganizationRole.Admin);

        await Assert.ThrowsAsync<ForbiddenException>(() => SetActiveAsync(
            admin.UserId, owner.OrganizationId, owner.UserId, false));
    }

    [Fact]
    public async Task AnExistingUser_CanAcceptWhileSignedIntoAnotherOrganization()
    {
        var (_, ownerA) = await RegisterOwnerAsync();
        var (emailB, ownerB) = await RegisterOwnerAsync();

        var token = await InviteAsync(ownerA.UserId, ownerA.OrganizationId, emailB, OrganizationRole.Staff);
        await AcceptAsync(token, null, null, callerOrganizationId: ownerB.OrganizationId);

        await using var ctx = TestDatabase.CreateContext(Clock, ownerA.OrganizationId);
        Assert.True(await ctx.Memberships.AnyAsync(m => m.UserId == ownerB.UserId));
    }

    [Fact]
    public async Task AnOwner_CanChangeAMembersRole()
    {
        var (_, owner) = await RegisterOwnerAsync();
        var member = await JoinAsync(owner, OrganizationRole.Staff);

        await ChangeRoleAsync(owner.UserId, owner.OrganizationId, member.UserId, OrganizationRole.Accountant);

        await using var ctx = TestDatabase.CreateContext(Clock, owner.OrganizationId);
        var membership = await ctx.Memberships.SingleAsync(m => m.UserId == member.UserId);
        Assert.Equal(OrganizationRole.Accountant, membership.Role);
    }
}