using Domain.Billing;
using Domain.Invoices;
using Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class OrganizationPaystackAccountConfiguration : IEntityTypeConfiguration<OrganizationPaystackAccount>
{
    public void Configure(EntityTypeBuilder<OrganizationPaystackAccount> builder)
    {
        builder.ToTable("OrganizationPaystackAccounts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SubaccountCode).IsRequired().HasMaxLength(100);
        builder.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.HasIndex(x => x.OrganizationId).IsUnique();
        builder.HasIndex(x => x.SubaccountCode).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InvoicePaymentCheckoutConfiguration : IEntityTypeConfiguration<InvoicePaymentCheckout>
{
    public void Configure(EntityTypeBuilder<InvoicePaymentCheckout> builder)
    {
        builder.ToTable("InvoicePaymentCheckouts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Reference).IsRequired().HasMaxLength(100);
        builder.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(30);
        builder.Property(x => x.AuthorizationUrl).HasMaxLength(1000);
        builder.HasIndex(x => x.Reference).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.InvoiceId });
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}
