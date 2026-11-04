using Application.Common.Interfaces;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public sealed class TestCurrentOrganization(Guid? organizationId) : ICurrentOrganization
{
    public Guid? OrganizationId => organizationId;
}

public static class TestDatabase
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=invoiceai_test;Username=invoiceai;Password=devpassword";

    public static AppDbContext CreateContext(TimeProvider clock, Guid? organizationId = null, Guid? userId = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .AddInterceptors(new AuditTimestampInterceptor(clock))
            .Options;

        var context = new AppDbContext(
            options, new TestCurrentOrganization(organizationId), new TestCurrentUser(userId));
        context.Database.Migrate();
        return context;
    }
}