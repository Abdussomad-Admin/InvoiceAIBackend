using Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.Name).IsRequired().HasMaxLength(200);
        builder.Property(o => o.CountryCode).IsRequired().HasMaxLength(2).IsFixedLength();
        builder.Property(o => o.BaseCurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(o => o.TimeZoneId).IsRequired().HasMaxLength(100);

        builder.Property(o => o.Email).HasMaxLength(254);
        builder.Property(o => o.Phone).HasMaxLength(30);
        builder.Property(o => o.TaxIdentificationNumber).HasMaxLength(50);
        builder.Property(o => o.AddressLine1).HasMaxLength(200);
        builder.Property(o => o.AddressLine2).HasMaxLength(200);
        builder.Property(o => o.City).HasMaxLength(100);
        builder.Property(o => o.Region).HasMaxLength(100);
        builder.Property(o => o.PostalCode).HasMaxLength(20);
        builder.Property(o => o.LogoStorageKey).HasMaxLength(500);

        builder.Property(o => o.CreatedAt).IsRequired();
    }
}