using Application.Common.Exceptions;
using Application.Customers;
using Domain.Common;
using Domain.Customers;
using Domain.Organizations;
using Infrastructure.Persistence.Repositories;

namespace Tests;

public class CustomerTests
{
    private static readonly FixedTimeProvider Clock =
        new(new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero));

    private static CustomerInput Input(string name) =>
        new(name, "billing@example.com", null, null, null, null, null, null, null, "NG", null);

    private static async Task<Guid> SeedOrganizationAsync()
    {
        await using var ctx = TestDatabase.CreateContext(Clock);
        var org = Organization.Create($"Org {Guid.NewGuid()}", "NG", "NGN", "Africa/Lagos");
        ctx.Organizations.Add(org);
        await ctx.SaveChangesAsync();
        return org.Id;
    }

    private static async Task<CustomerDto> CreateAsync(Guid organizationId, string name)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, organizationId);
        var handler = new CreateCustomerHandler(
            new CustomerRepository(ctx), ctx, new TestCurrentOrganization(organizationId));
        return await handler.Handle(Input(name), CancellationToken.None);
    }

    [Fact]
    public void Create_WithBlankName_Throws()
        => Assert.Throws<DomainException>(() => Customer.Create(Guid.NewGuid(), "  "));

    [Fact]
    public async Task Customers_AreInvisibleToOtherOrganizations()
    {
        var orgA = await SeedOrganizationAsync();
        var orgB = await SeedOrganizationAsync();
        var created = await CreateAsync(orgA, $"Zenith {Guid.NewGuid()}");

        await using (var ctxA = TestDatabase.CreateContext(Clock, orgA))
        {
            var repo = new CustomerRepository(ctxA);
            var found = await new ListCustomersHandler(repo)
                .Handle(new ListCustomersQuery(null, false, 1, 100), CancellationToken.None);
            Assert.Contains(found.Items, c => c.Id == created.Id);
        }

        await using (var ctxB = TestDatabase.CreateContext(Clock, orgB))
        {
            var repo = new CustomerRepository(ctxB);
            var found = await new ListCustomersHandler(repo)
                .Handle(new ListCustomersQuery(null, false, 1, 100), CancellationToken.None);
            Assert.DoesNotContain(found.Items, c => c.Id == created.Id);

            await Assert.ThrowsAsync<NotFoundException>(
                () => new GetCustomerHandler(repo).Handle(created.Id, CancellationToken.None));
        }
    }

    [Fact]
    public async Task SavingACustomerForAnotherOrganization_IsRejected()
    {
        var orgA = await SeedOrganizationAsync();
        var orgB = await SeedOrganizationAsync();

        await using var ctx = TestDatabase.CreateContext(Clock, orgA);
        ctx.Customers.Add(Customer.Create(orgB, "Sneaky Ltd"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
    }
}