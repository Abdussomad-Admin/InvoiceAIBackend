using Application.Common.Interfaces;
using Domain.Audit;
using Domain.Bills;
using Domain.Common;
using Domain.Customers;
using Domain.Documents;
using Domain.Invoices;
using Domain.Organizations;
using Domain.Payments;
using Domain.Suppliers;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Globalization;
using System.Reflection;

namespace Infrastructure.Persistence;

public class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ICurrentOrganization currentOrganization,
    ICurrentUser currentUser) : DbContext(options), IUnitOfWork, ITenantWriteBypass, ITransactionRunner
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<BillPayment> BillPayments => Set<BillPayment>();
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();
    public DbSet<DocumentBlob> DocumentBlobs => Set<DocumentBlob>();
    public DbSet<DocumentExtraction> DocumentExtractions => Set<DocumentExtraction>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierBill> SupplierBills => Set<SupplierBill>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

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

    public async Task RunInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        await action();
        await transaction.CommitAsync(cancellationToken);
    }

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

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareForSave();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareForSave();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareForSave()
    {
        if (ChangeTracker.Entries<AuditEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Audit entries are append-only.");

        CaptureAudit();
        EnforceTenantOnWrites();
    }

    // Write guard: when a request has a current organization, it may only save rows for that organization.
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

    // ---- Audit log: written in the same save (and transaction) as the change it describes ----

    private readonly record struct AuditInfo(
        Guid OrganizationId, string Action, string EntityType, Guid EntityId, string? Details);

    private void CaptureAudit()
    {
        var userId = currentUser.UserId;
        var changed = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToList();

        foreach (var entry in changed)
        {
            var info = Describe(entry);
            if (info is null) continue;

            AuditEntries.Add(AuditEntry.Create(
                info.Value.OrganizationId, userId, info.Value.Action,
                info.Value.EntityType, info.Value.EntityId, info.Value.Details));
        }
    }

    private static bool Changed(PropertyEntry property)
        => property.IsModified && !Equals(property.OriginalValue, property.CurrentValue);

    private static string Fmt(object? amount)
        => Convert.ToDecimal(amount, CultureInfo.InvariantCulture).ToString("0.00", CultureInfo.InvariantCulture);

    private static AuditInfo? Describe(EntityEntry entry)
    {
        var added = entry.State == EntityState.Added;

        switch (entry.Entity)
        {
            case Invoice i when added:
                return new(i.OrganizationId, "Invoice.Created", "Invoice", i.Id, $"Total {Fmt(i.Total)} {i.CurrencyCode}");

            case Invoice i:
                {
                    var status = entry.Property(nameof(Invoice.Status));
                    var paid = entry.Property(nameof(Invoice.AmountPaid));

                    if (Changed(paid))
                        return new(i.OrganizationId, "Invoice.PaymentChanged", "Invoice", i.Id,
                            $"Amount paid {Fmt(paid.OriginalValue)} -> {Fmt(paid.CurrentValue)}; status {status.OriginalValue} -> {status.CurrentValue}");

                    if (Changed(status))
                    {
                        var number = i.Number is null ? "" : $" ({i.Number})";
                        var reason = i.Status == InvoiceStatus.Void ? $"; reason: {i.VoidReason}" : "";
                        return new(i.OrganizationId, "Invoice.StatusChanged", "Invoice", i.Id,
                            $"{status.OriginalValue} -> {status.CurrentValue}{number}{reason}");
                    }

                    var sent = entry.Property(nameof(Invoice.SentCount));
                    if (Changed(sent))
                        return new(i.OrganizationId, "Invoice.Sent", "Invoice", i.Id,
                            $"Sent to {i.LastSentTo} (send #{i.SentCount})");

                    return new(i.OrganizationId, "Invoice.Updated", "Invoice", i.Id, $"Total {Fmt(i.Total)} {i.CurrencyCode}");
                }

            case Payment p when added:
                return new(p.OrganizationId, "Payment.Recorded", "Payment", p.Id,
                    $"{Fmt(p.Amount)} {p.CurrencyCode} on invoice {p.InvoiceId} via {p.Method}");

            case Payment p:
                return Changed(entry.Property(nameof(Payment.ReversedAt)))
                    ? new(p.OrganizationId, "Payment.Reversed", "Payment", p.Id,
                        $"{Fmt(p.Amount)} {p.CurrencyCode}; reason: {p.ReversalReason}")
                    : null;

            case Membership m when added:
                return new(m.OrganizationId, "Member.Added", "Membership", m.Id, $"User {m.UserId} as {m.Role}");

            case Membership m:
                {
                    var role = entry.Property(nameof(Membership.Role));
                    var active = entry.Property(nameof(Membership.IsActive));

                    if (Changed(role))
                        return new(m.OrganizationId, "Member.RoleChanged", "Membership", m.Id,
                            $"{role.OriginalValue} -> {role.CurrentValue}");
                    if (Changed(active))
                        return new(m.OrganizationId, m.IsActive ? "Member.Activated" : "Member.Deactivated",
                            "Membership", m.Id, $"User {m.UserId}");
                    return null;
                }

            case Customer c when added:
                return new(c.OrganizationId, "Customer.Created", "Customer", c.Id, c.Name);

            case Customer c:
                {
                    var archived = entry.Property(nameof(Customer.IsArchived));
                    var action = Changed(archived)
                        ? (c.IsArchived ? "Customer.Archived" : "Customer.Restored")
                        : "Customer.Updated";
                    return new(c.OrganizationId, action, "Customer", c.Id, c.Name);
                }

            case Organization o when added:
                return new(o.Id, "Organization.Created", "Organization", o.Id, o.Name);

            case Organization o:
                return new(o.Id, "Organization.Updated", "Organization", o.Id, o.Name);
            case SupplierBill b when added:
                return new(b.OrganizationId, "Bill.Created", "SupplierBill", b.Id,
                    $"{b.Number} total {Fmt(b.Total)} {b.CurrencyCode}");

            case Supplier s when added:
                return new(s.OrganizationId, "Supplier.Created", "Supplier", s.Id, s.Name);

            case Supplier s:
                {
                    var archived = entry.Property(nameof(Supplier.IsArchived));
                    var action = Changed(archived)
                        ? (s.IsArchived ? "Supplier.Archived" : "Supplier.Restored")
                        : "Supplier.Updated";
                    return new(s.OrganizationId, action, "Supplier", s.Id, s.Name);
                }

            case SupplierBill b:
                {
                    var status = entry.Property(nameof(SupplierBill.Status));
                    var paid = entry.Property(nameof(SupplierBill.AmountPaid));

                    if (Changed(paid))
                        return new(b.OrganizationId, "Bill.PaymentChanged", "SupplierBill", b.Id,
                            $"Amount paid {Fmt(paid.OriginalValue)} -> {Fmt(paid.CurrentValue)}; status {status.OriginalValue} -> {status.CurrentValue}");

                    if (Changed(status))
                    {
                        var reason = b.Status == BillStatus.Void ? $"; reason: {b.VoidReason}" : "";
                        return new(b.OrganizationId, "Bill.StatusChanged", "SupplierBill", b.Id,
                            $"{status.OriginalValue} -> {status.CurrentValue} ({b.Number}){reason}");
                    }

                    return new(b.OrganizationId, "Bill.Updated", "SupplierBill", b.Id,
                        $"{b.Number} total {Fmt(b.Total)} {b.CurrencyCode}");
                }

            case BillPayment p when added:
                return new(p.OrganizationId, "BillPayment.Recorded", "BillPayment", p.Id,
                    $"{Fmt(p.Amount)} {p.CurrencyCode} on bill {p.BillId} via {p.Method}");

            case BillPayment p:
                return Changed(entry.Property(nameof(BillPayment.ReversedAt)))
                    ? new(p.OrganizationId, "BillPayment.Reversed", "BillPayment", p.Id,
                        $"{Fmt(p.Amount)} {p.CurrencyCode}; reason: {p.ReversalReason}")
                    : null;

            case SourceDocument d when added:
                return new(d.OrganizationId, "Document.Uploaded", "SourceDocument", d.Id,
                    $"{d.FileName} ({d.SizeBytes} bytes)");

            case SourceDocument d:
                return Changed(entry.Property(nameof(SourceDocument.Status)))
                    ? new(d.OrganizationId, $"Document.{d.Status}", "SourceDocument", d.Id,
                        d.RejectionReason is null ? d.FileName : $"{d.FileName}; reason: {d.RejectionReason}")
                    : null;

            default:
                return null;
        }
    }
}