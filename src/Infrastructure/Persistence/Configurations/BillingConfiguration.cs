using Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class OrganizationSubscriptionConfiguration : IEntityTypeConfiguration<OrganizationSubscription>
{
    public void Configure(EntityTypeBuilder<OrganizationSubscription> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.PlanCode).HasMaxLength(80).IsRequired();
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.PaystackCustomerCode).HasMaxLength(100);
        b.Property(x => x.PaystackSubscriptionCode).HasMaxLength(100);
        b.HasIndex(x => x.OrganizationId).IsUnique();
        b.HasIndex(x => x.PaystackSubscriptionCode).IsUnique().HasFilter("\"PaystackSubscriptionCode\" IS NOT NULL");
    }
}

public sealed class BillingCheckoutConfiguration : IEntityTypeConfiguration<BillingCheckout>
{
    public void Configure(EntityTypeBuilder<BillingCheckout> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.PlanCode).HasMaxLength(80).IsRequired();
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(120).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.AuthorizationUrl).HasMaxLength(2048);
        b.HasIndex(x => x.Reference).IsUnique();
        b.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
    }
}
