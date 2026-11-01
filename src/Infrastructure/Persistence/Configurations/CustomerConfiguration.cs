using Domain.Customers;
using Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Email).HasMaxLength(254);
        builder.Property(c => c.Phone).HasMaxLength(30);
        builder.Property(c => c.TaxIdentificationNumber).HasMaxLength(50);
        builder.Property(c => c.AddressLine1).HasMaxLength(200);
        builder.Property(c => c.AddressLine2).HasMaxLength(200);
        builder.Property(c => c.City).HasMaxLength(100);
        builder.Property(c => c.Region).HasMaxLength(100);
        builder.Property(c => c.PostalCode).HasMaxLength(20);
        builder.Property(c => c.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.Property(c => c.Notes).HasMaxLength(2000);
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(c => c.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.OrganizationId, c.Name });
    }
}