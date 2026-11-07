using Domain.Bills;
using Domain.Organizations;
using Domain.Suppliers;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class BillPaymentConfiguration : IEntityTypeConfiguration<BillPayment>
{
    public void Configure(EntityTypeBuilder<BillPayment> builder)
    {
        builder.ToTable("BillPayments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.CurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.Method).HasConversion<string>().IsRequired().HasMaxLength(30);
        builder.Property(p => p.Reference).HasMaxLength(100);
        builder.Property(p => p.Notes).HasMaxLength(1000);
        builder.Property(p => p.ReversalReason).HasMaxLength(500);
        builder.Property(p => p.CreatedAt).IsRequired();

        builder.Ignore(p => p.IsReversed);

        builder.HasOne<Organization>().WithMany().HasForeignKey(p => p.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SupplierBill>().WithMany().HasForeignKey(p => p.BillId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Supplier>().WithMany().HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.ReversedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.BillId);
        builder.HasIndex(p => p.SupplierId);
        builder.HasIndex(p => new { p.OrganizationId, p.PaidOn });

        // The same reference cannot be recorded twice on one bill, unless the earlier payment was reversed.
        builder.HasIndex(p => new { p.BillId, p.Reference })
            .IsUnique()
            .HasFilter("\"Reference\" IS NOT NULL AND \"ReversedAt\" IS NULL");
    }
}