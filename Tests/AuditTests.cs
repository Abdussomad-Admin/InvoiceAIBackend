using Domain.Organizations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public class AuditTests
{
    private static readonly FixedTimeProvider Clock = PaymentTestData.Clock;

    [Fact]
    public async Task InvoiceAndPaymentLifecycle_LeavesAnAuditTrail()
    {
        var seed = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed);
        var payment = await PaymentTestData.RecordAsync(seed, invoice.Id, 100.00m);
        await PaymentTestData.ReverseAsync(seed, payment.Id, "Bounced");

        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var trail = await ctx.AuditEntries.ToListAsync();

        Assert.Contains(trail, a => a.Action == "Invoice.Created" && a.EntityId == invoice.Id);
        Assert.Contains(trail, a =>
            a.Action == "Invoice.StatusChanged" && a.EntityId == invoice.Id &&
            a.Details == "Draft -> Issued (INV-0001)");
        Assert.Contains(trail, a =>
            a.Action == "Invoice.PaymentChanged" && a.EntityId == invoice.Id &&
            a.Details!.StartsWith("Amount paid 0.00 -> 100.00"));
        Assert.Contains(trail, a => a.Action == "Payment.Recorded" && a.EntityId == payment.Id);
        Assert.Contains(trail, a => a.Action == "Payment.Reversed" && a.EntityId == payment.Id);

        // Who did it: every entry made by a signed-in request carries the user.
        Assert.All(trail.Where(a => a.EntityId == invoice.Id), a => Assert.Equal(seed.UserId, a.UserId));
    }

    [Fact]
    public async Task AuditEntries_AreInvisibleToOtherOrganizations()
    {
        var seedA = await PaymentTestData.SeedAsync();
        var seedB = await PaymentTestData.SeedAsync();
        await PaymentTestData.IssueInvoiceAsync(seedA);

        await using var ctxB = TestDatabase.CreateContext(Clock, seedB.OrganizationId, seedB.UserId);
        var visible = await ctxB.AuditEntries.ToListAsync();
        Assert.DoesNotContain(visible, a => a.OrganizationId == seedA.OrganizationId);

        // The entries really exist: the filter is what hides them.
        var existing = await ctxB.AuditEntries.IgnoreQueryFilters()
            .CountAsync(a => a.OrganizationId == seedA.OrganizationId);
        Assert.True(existing > 0);
    }

    [Fact]
    public async Task AuditEntries_CannotBeDeleted()
    {
        var seed = await PaymentTestData.SeedAsync();

        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var entry = await ctx.AuditEntries.FirstAsync();
        ctx.AuditEntries.Remove(entry);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task MembershipChanges_AreAudited()
    {
        var seed = await PaymentTestData.SeedAsync();
        Guid membershipId;

        await using (var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId))
        {
            var member = User.Register($"{Guid.NewGuid()}@example.com", "hash", "Team Member");
            ctx.Users.Add(member);
            var membership = Membership.Create(member.Id, seed.OrganizationId, OrganizationRole.Staff);
            ctx.Memberships.Add(membership);
            await ctx.SaveChangesAsync();
            membershipId = membership.Id;
        }

        await using (var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId))
        {
            var membership = await ctx.Memberships.SingleAsync(m => m.Id == membershipId);
            membership.ChangeRole(OrganizationRole.Admin);
            await ctx.SaveChangesAsync();
        }

        await using var read = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var trail = await read.AuditEntries.Where(a => a.EntityId == membershipId).ToListAsync();

        Assert.Contains(trail, a => a.Action == "Member.Added");
        Assert.Contains(trail, a => a.Action == "Member.RoleChanged" && a.Details == "Staff -> Admin");
    }
}