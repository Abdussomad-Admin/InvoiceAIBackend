using Application.Banking;
using Domain.Banking;
using Domain.Organizations;
using Domain.Users;
using Infrastructure.Persistence.Repositories;

namespace Tests;

public sealed class BankStatementCoverageTests
{
    [Fact]
    public async Task Coverage_IsScopedToTheCurrentOrganization_AndCountsEachResolutionState()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var firstUser = User.Register($"coverage-a-{Guid.NewGuid()}@example.com", "hash", "Coverage A");
        var secondUser = User.Register($"coverage-b-{Guid.NewGuid()}@example.com", "hash", "Coverage B");
        var firstOrganization = Organization.Create($"Coverage A {Guid.NewGuid()}", "NG", "NGN", "Africa/Lagos");
        var secondOrganization = Organization.Create($"Coverage B {Guid.NewGuid()}", "NG", "NGN", "Africa/Lagos");
        var firstBatch = BankStatementBatch.Create(firstOrganization.Id, firstUser.Id, "first.csv", Guid.NewGuid().ToString("N"));
        var otherBatch = BankStatementBatch.Create(secondOrganization.Id, secondUser.Id, "other.csv", Guid.NewGuid().ToString("N"));
        var date = new DateOnly(2026, 11, 1);
        var unmatched = BankStatementLine.Create(firstOrganization.Id, firstBatch.Id, 2, date, "Unmatched transfer", 100m, "NGN", null);
        var ignored = BankStatementLine.Create(firstOrganization.Id, firstBatch.Id, 3, date, "Reviewed transfer", -50m, "NGN", null);
        ignored.Ignore("Personal transfer", firstUser.Id, clock.GetUtcNow());
        firstBatch.AddLine(unmatched);
        firstBatch.AddLine(ignored);

        var otherLine = BankStatementLine.Create(secondOrganization.Id, otherBatch.Id, 2, date, "Other workspace", 25m, "NGN", null);
        otherLine.Ignore("Reviewed", secondUser.Id, clock.GetUtcNow());
        otherBatch.AddLine(otherLine);

        await using (var setup = TestDatabase.CreateContext(clock))
        {
            setup.Users.AddRange(firstUser, secondUser);
            setup.Organizations.AddRange(firstOrganization, secondOrganization);
            setup.BankStatementBatches.AddRange(firstBatch, otherBatch);
            await setup.SaveChangesAsync();
        }

        await using var workspace = TestDatabase.CreateContext(clock, firstOrganization.Id, firstUser.Id);
        var coverage = await new BankStatementRepository(workspace).GetCoverageAsync();

        Assert.Equal(new BankReconciliationCoverageDto(2, 0, 1, 1), coverage);
    }

    [Fact]
    public async Task Coverage_ReturnsEmptyWhenWorkspaceHasNoImportedStatementLines()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        var organization = Organization.Create($"No statements {Guid.NewGuid()}", "NG", "NGN", "Africa/Lagos");

        await using (var setup = TestDatabase.CreateContext(clock))
        {
            setup.Organizations.Add(organization);
            await setup.SaveChangesAsync();
        }

        await using var workspace = TestDatabase.CreateContext(clock, organization.Id);
        var coverage = await new BankStatementRepository(workspace).GetCoverageAsync();

        Assert.Equal(BankReconciliationCoverageDto.Empty, coverage);
    }
}
