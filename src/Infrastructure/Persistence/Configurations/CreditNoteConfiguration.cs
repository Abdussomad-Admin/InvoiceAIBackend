using Domain.Invoices;
using Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.ToTable("CreditNotes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Number).IsRequired().HasMaxLength(50);
        builder.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(x => x.BaseCurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(x => x.ExchangeRateToBase).HasPrecision(18, 8);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(500);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.Number }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.InvoiceId, x.IssuedOn });
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RecurringInvoiceConfiguration : IEntityTypeConfiguration<RecurringInvoiceSchedule>
{
    public void Configure(EntityTypeBuilder<RecurringInvoiceSchedule> builder)
    {
        builder.ToTable("RecurringInvoiceSchedules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Description).IsRequired().HasMaxLength(200);
        builder.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(x => x.LinesJson).IsRequired().HasColumnType("jsonb");
        builder.Property(x => x.Frequency).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.BaseCurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(x => x.ExchangeRateToBase).HasPrecision(18, 8);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.IsActive, x.NextRunOn });
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Domain.Customers.Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}
