using Domain.Organizations;
using Domain.Users;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public class RepositoryTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task MembershipRepository_FindsActiveMembershipsAndOwnersCount()
    {
        var clock = new FixedTimeProvider(FixedNow);
        var user = User.Register($"{Guid.NewGuid()}@example.com", "hash", "Ada Obi");
        var org = Organization.Create($"Acme {Guid.NewGuid()}", "NG", "NGN", "Africa/Lagos");
        var membership = Membership.Create(user.Id, org.Id, OrganizationRole.Owner);

        await using (var context = TestDatabase.CreateContext(clock))
        {
            new UserRepository(context).Add(user);
            new OrganizationRepository(context).Add(org);
            new MembershipRepository(context).Add(membership);
            await context.SaveChangesAsync();
        }

        await using var readContext = TestDatabase.CreateContext(clock, org.Id);
        var memberships = new MembershipRepository(readContext);

        var found = await memberships.GetActiveForUserAsync(user.Id);
        var owners = await memberships.CountActiveOwnersAsync(org.Id);

        var single = Assert.Single(found);
        Assert.Equal(org.Id, single.OrganizationId);
        Assert.Equal(OrganizationRole.Owner, single.Role);
        Assert.Equal(1, owners);
    }

    [Fact]
    public async Task AddingTheSameMembershipTwice_Throws()
    {
        var clock = new FixedTimeProvider(FixedNow);
        var user = User.Register($"{Guid.NewGuid()}@example.com", "hash", "Ada Obi");
        var org = Organization.Create($"Acme {Guid.NewGuid()}", "NG", "NGN", "Africa/Lagos");

        await using (var context = TestDatabase.CreateContext(clock))
        {
            new UserRepository(context).Add(user);
            new OrganizationRepository(context).Add(org);
            new MembershipRepository(context).Add(
                Membership.Create(user.Id, org.Id, OrganizationRole.Staff));
            await context.SaveChangesAsync();
        }

        await using var secondContext = TestDatabase.CreateContext(clock);
        new MembershipRepository(secondContext).Add(
            Membership.Create(user.Id, org.Id, OrganizationRole.Admin));

        await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());
    }
}