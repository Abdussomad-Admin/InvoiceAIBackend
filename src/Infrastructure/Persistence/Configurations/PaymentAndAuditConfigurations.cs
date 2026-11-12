using Domain.Audit;
using Domain.Customers;
using Domain.Invoices;
using Domain.Organizations;
using Domain.Payments;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.CurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(p => p.ExchangeRateToBase).HasPrecision(18, 8);
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.Method).HasConversion<string>().IsRequired().HasMaxLength(30);
        builder.Property(p => p.Reference).HasMaxLength(100);
        builder.Property(p => p.Notes).HasMaxLength(1000);
        builder.Property(p => p.ReversalReason).HasMaxLength(500);
        builder.Property(p => p.CreatedAt).IsRequired();

        builder.Ignore(p => p.IsReversed);

        builder.HasOne<Organization>().WithMany().HasForeignKey(p => p.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Invoice>().WithMany().HasForeignKey(p => p.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(p => p.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.ReversedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.InvoiceId);
        builder.HasIndex(p => p.CustomerId);
        builder.HasIndex(p => new { p.OrganizationId, p.PaidOn });

        // The same reference cannot be recorded twice on one invoice, unless the earlier payment was reversed.
        builder.HasIndex(p => new { p.InvoiceId, p.Reference })
            .IsUnique()
            .HasFilter("\"Reference\" IS NOT NULL AND \"ReversedAt\" IS NULL");
    }
}

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Action).IsRequired().HasMaxLength(100);
        builder.Property(a => a.EntityType).IsRequired().HasMaxLength(50);
        builder.Property(a => a.Details).HasMaxLength(1000);
        builder.Property(a => a.CreatedAt).IsRequired();

        builder.HasOne<Organization>().WithMany().HasForeignKey(a => a.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.OrganizationId, a.CreatedAt });
        builder.HasIndex(a => new { a.OrganizationId, a.EntityType, a.EntityId });
    }
}
