using Application.Common.Interfaces;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Host;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Used only by migration commands (Add-Migration, Update-Database).
        // Defaults to your LOCAL database; set ConnectionStrings__Default to target another one.
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=invoiceai;Username=invoiceai;Password=devpassword";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options, new DesignTimeCurrentOrganization());
    }

    private sealed class DesignTimeCurrentOrganization : ICurrentOrganization
    {
        public Guid? OrganizationId => null;
    }
}