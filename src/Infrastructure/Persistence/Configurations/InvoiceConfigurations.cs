using Domain.Customers;
using Domain.Invoices;
using Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.Number).HasMaxLength(50);
        builder.Property(i => i.PublicPaymentToken).HasMaxLength(64);
        builder.HasIndex(i => i.PublicPaymentToken).IsUnique();
        builder.Property(c => c.LastSentTo).HasMaxLength(254);
        builder.Property(i => i.CurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(i => i.Status).HasConversion<string>().IsRequired().HasMaxLength(30);
        builder.Property(i => i.Notes).HasMaxLength(2000);
        builder.Property(i => i.VoidReason).HasMaxLength(500);

        builder.Property(i => i.Subtotal).HasPrecision(18, 2);
        builder.Property(i => i.DiscountTotal).HasPrecision(18, 2);
        builder.Property(i => i.TaxTotal).HasPrecision(18, 2);
        builder.Property(i => i.Total).HasPrecision(18, 2);
        builder.Property(i => i.AmountPaid).HasPrecision(18, 2);
        builder.Property(i => i.CreatedAt).IsRequired();

        builder.Ignore(i => i.Balance);

        builder.HasOne<Organization>().WithMany().HasForeignKey(i => i.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(i => i.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);

        // No two invoices in one organization share a number (drafts have none, so they never clash).
        builder.HasIndex(i => new { i.OrganizationId, i.Number }).IsUnique();
        builder.HasIndex(i => new { i.OrganizationId, i.Status });
        builder.HasIndex(i => new { i.OrganizationId, i.DueDate });
        builder.HasIndex(i => i.CustomerId);
    }
}

public class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("InvoiceLines");
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

        builder.HasIndex(l => l.InvoiceId);
    }
}

public class InvoiceCounterConfiguration : IEntityTypeConfiguration<InvoiceCounter>
{
    public void Configure(EntityTypeBuilder<InvoiceCounter> builder)
    {
        builder.ToTable("InvoiceCounters");
        builder.HasKey(c => c.OrganizationId);
        builder.Property(c => c.OrganizationId).ValueGeneratedNever();
        builder.Property(c => c.LastNumber).IsRequired();


        builder.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
