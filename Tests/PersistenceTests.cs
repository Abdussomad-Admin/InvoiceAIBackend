using Domain.Organizations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public class PersistenceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task SavingAnOrganization_StampsCreatedAt_AndCanBeReadBack()
    {
        var clock = new FixedTimeProvider(FixedNow);
        var org = Organization.Create($"Acme {Guid.NewGuid()}", "NG", "NGN", "Africa/Lagos");

        await using (var writeContext = TestDatabase.CreateContext(clock))
        {
            writeContext.Organizations.Add(org);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = TestDatabase.CreateContext(clock);
        var saved = await readContext.Organizations.SingleAsync(o => o.Id == org.Id);

        Assert.Equal(org.Name, saved.Name);
        Assert.Equal(FixedNow, saved.CreatedAt);
        Assert.Null(saved.UpdatedAt);
    }

    [Fact]
    public async Task SavingTwoUsersWithTheSameEmail_Throws()
    {
        var clock = new FixedTimeProvider(FixedNow);
        var email = $"{Guid.NewGuid()}@example.com";

        await using var firstContext = TestDatabase.CreateContext(clock);
        firstContext.Users.Add(User.Register(email, "hash1", "First Person"));
        await firstContext.SaveChangesAsync();

        await using var secondContext = TestDatabase.CreateContext(clock);
        secondContext.Users.Add(User.Register(email, "hash2", "Second Person"));

        await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync());
    }
}