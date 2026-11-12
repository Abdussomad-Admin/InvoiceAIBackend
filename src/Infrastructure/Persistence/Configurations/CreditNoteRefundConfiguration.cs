using Domain.Invoices;
using Domain.Organizations;
using Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class CreditNoteRefundConfiguration : IEntityTypeConfiguration<CreditNoteRefund>
{
    public void Configure(EntityTypeBuilder<CreditNoteRefund> builder)
    {
        builder.ToTable("CreditNoteRefunds");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ProviderReference).HasMaxLength(100);
        builder.Property(x => x.FailureReason).HasMaxLength(500);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.ProviderReference }).IsUnique()
            .HasFilter("\"ProviderReference\" IS NOT NULL");
        builder.HasIndex(x => new { x.OrganizationId, x.InvoiceId, x.CreatedAt });
        builder.HasIndex(x => x.PaymentId);
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CreditNote>().WithMany().HasForeignKey(x => x.CreditNoteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}
