using System.Reflection;
using Application.Common.Interfaces;
using Domain.Common;
using Domain.Customers;
using Domain.Organizations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ICurrentOrganization currentOrganization) : DbContext(options), IUnitOfWork, ITenantWriteBypass
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Invitation> Invitations => Set<Invitation>();

    private Guid? CurrentOrganizationId => currentOrganization.OrganizationId;
    private bool _allowCrossTenantWrites;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Apply the tenant filter to EVERY entity wearing the ITenantOwned label,
        // so a future entity can never be forgotten.
        var applyFilter = typeof(AppDbContext).GetMethod(
            nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)))
        {
            applyFilter.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder) where T : class, ITenantOwned
        => modelBuilder.Entity<T>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);

    // Narrow, visible exemption from the write guard (see ITenantWriteBypass).
    public IDisposable Allow()
    {
        _allowCrossTenantWrites = true;
        return new Reset(this);
    }

    private sealed class Reset(AppDbContext context) : IDisposable
    {
        public void Dispose() => context._allowCrossTenantWrites = false;
    }

    // Write guard: when a request has a current organization, it may only save rows for that organization.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantOnWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceTenantOnWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceTenantOnWrites()
    {
        var current = CurrentOrganizationId;
        if (current is null || _allowCrossTenantWrites) return;

        var violation = ChangeTracker.Entries<ITenantOwned>()
            .Any(e => e.State is EntityState.Added or EntityState.Modified
                      && e.Entity.OrganizationId != current);

        if (violation)
            throw new InvalidOperationException("Attempted to write data belonging to another organization.");
    }
}