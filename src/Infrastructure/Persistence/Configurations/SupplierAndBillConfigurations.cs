using Domain.Bills;
using Domain.Organizations;
using Domain.Suppliers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Email).HasMaxLength(254);
        builder.Property(s => s.Phone).HasMaxLength(30);
        builder.Property(s => s.TaxIdentificationNumber).HasMaxLength(50);
        builder.Property(s => s.AddressLine1).HasMaxLength(200);
        builder.Property(s => s.AddressLine2).HasMaxLength(200);
        builder.Property(s => s.City).HasMaxLength(100);
        builder.Property(s => s.Region).HasMaxLength(100);
        builder.Property(s => s.PostalCode).HasMaxLength(20);
        builder.Property(s => s.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.Property(s => s.Notes).HasMaxLength(2000);
        builder.Property(s => s.CreatedAt).IsRequired();

        builder.HasOne<Organization>().WithMany().HasForeignKey(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.OrganizationId, s.Name });
    }
}

public class SupplierBillConfiguration : IEntityTypeConfiguration<SupplierBill>
{
    public void Configure(EntityTypeBuilder<SupplierBill> builder)
    {
        builder.ToTable("SupplierBills");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.Number).IsRequired().HasMaxLength(100);
        builder.Property(b => b.NumberKey).IsRequired().HasMaxLength(100);
        builder.Property(b => b.CurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(b => b.BaseCurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(b => b.ExchangeRateToBase).HasPrecision(18, 8);
        builder.Property(b => b.Status).HasConversion<string>().IsRequired().HasMaxLength(30);
        builder.Property(b => b.Notes).HasMaxLength(2000);
        builder.Property(b => b.VoidReason).HasMaxLength(500);

        builder.Property(b => b.Subtotal).HasPrecision(18, 2);
        builder.Property(b => b.DiscountTotal).HasPrecision(18, 2);
        builder.Property(b => b.TaxTotal).HasPrecision(18, 2);
        builder.Property(b => b.Total).HasPrecision(18, 2);
        builder.Property(b => b.AmountPaid).HasPrecision(18, 2);
        builder.Property(b => b.CreatedAt).IsRequired();

        builder.Ignore(b => b.Balance);

        builder.HasOne<Organization>().WithMany().HasForeignKey(b => b.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Supplier>().WithMany().HasForeignKey(b => b.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(b => b.Lines).WithOne().HasForeignKey(l => l.BillId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(b => b.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);

        // The same supplier cannot have two live bills with the same number. Voided bills do not count,
        // so a number can be re-entered after a void. This is the database backstop for the duplicate rule.
        builder.HasIndex(b => new { b.OrganizationId, b.SupplierId, b.NumberKey })
            .IsUnique()
            .HasFilter("\"Status\" <> 'Void'");

        builder.HasIndex(b => new { b.OrganizationId, b.Status });
        builder.HasIndex(b => new { b.OrganizationId, b.DueDate });
        builder.HasIndex(b => b.SupplierId);
    }
}

public class BillLineConfiguration : IEntityTypeConfiguration<BillLine>
{
    public void Configure(EntityTypeBuilder<BillLine> builder)
    {
        builder.ToTable("BillLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.Description).IsRequired().HasMaxLength(500);
        builder.Property(l => l.Quantity).HasPrecision(18, 4);
        builder.Property(l => l.UnitPrice).HasPrecision(18, 4);
        builder.Property(l => l.DiscountPercent).HasPrecision(5, 2);
        builder.Property(l => l.TaxRatePercent).HasPrecision(5, 2);
        builder.Property(l => l.LineSubtotal).HasPrecision(18, 2);
        builder.Property(l => l.DiscountAmount).HasPrecision(18, 2);
        builder.Property(l => l.TaxAmount).HasPrecision(18, 2);
        builder.Property(l => l.LineTotal).HasPrecision(18, 2);
        builder.Property(l => l.CreatedAt).IsRequired();

        builder.Ignore(l => l.NetAmount);

        builder.HasIndex(l => l.BillId);
    }
}
