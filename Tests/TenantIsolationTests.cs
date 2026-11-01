using Domain.Organizations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public class TenantIsolationTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Memberships_AreOnlyVisibleInsideTheirOwnOrganization()
    {
        var clock = new FixedTimeProvider(FixedNow);

        var userA = User.Register($"{Guid.NewGuid()}@example.com", "hash", "User A");
        var userB = User.Register($"{Guid.NewGuid()}@example.com", "hash", "User B");
        var orgA = Organization.Create($"Org A {Guid.NewGuid()}", "NG", "NGN", "Africa/Lagos");
        var orgB = Organization.Create($"Org B {Guid.NewGuid()}", "NG", "NGN", "Africa/Lagos");

        await using (var setup = TestDatabase.CreateContext(clock))
        {
            setup.Users.AddRange(userA, userB);
            setup.Organizations.AddRange(orgA, orgB);
            setup.Memberships.Add(Membership.Create(userA.Id, orgA.Id, OrganizationRole.Owner));
            setup.Memberships.Add(Membership.Create(userB.Id, orgB.Id, OrganizationRole.Owner));
            await setup.SaveChangesAsync();
        }

        await using (var insideA = TestDatabase.CreateContext(clock, orgA.Id))
        {
            var visible = await insideA.Memberships.ToListAsync();

            Assert.Single(visible);
            Assert.Equal(orgA.Id, visible[0].OrganizationId);
            Assert.DoesNotContain(visible, m => m.OrganizationId == orgB.Id);
        }

        await using (var insideB = TestDatabase.CreateContext(clock, orgB.Id))
        {
            var seesA = await insideB.Memberships.AnyAsync(m => m.UserId == userA.Id);
            Assert.False(seesA);
        }

        await using (var noOrganization = TestDatabase.CreateContext(clock))
        {
            var visible = await noOrganization.Memberships
                .Where(m => m.UserId == userA.Id || m.UserId == userB.Id)
                .ToListAsync();

            Assert.Empty(visible);

            var existing = await noOrganization.Memberships
                .IgnoreQueryFilters()
                .CountAsync(m => m.UserId == userA.Id || m.UserId == userB.Id);

            Assert.Equal(2, existing);
        }
    }
}