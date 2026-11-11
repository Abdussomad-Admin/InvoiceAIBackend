using Domain.Banking;
using Domain.Organizations;
using Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class BankStatementBatchConfiguration : IEntityTypeConfiguration<BankStatementBatch>
{
    public void Configure(EntityTypeBuilder<BankStatementBatch> builder)
    {
        builder.ToTable("BankStatementBatches");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FileName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.FileHash).IsRequired().HasMaxLength(64);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.StatementBatchId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.FileHash }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
    }
}

public sealed class BankStatementLineConfiguration : IEntityTypeConfiguration<BankStatementLine>
{
    public void Configure(EntityTypeBuilder<BankStatementLine> builder)
    {
        builder.ToTable("BankStatementLines");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Description).IsRequired().HasMaxLength(500);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(x => x.Reference).HasMaxLength(100);
        builder.Property(x => x.Status).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(x => x.ResolutionNote).HasMaxLength(500);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Payment>().WithMany().HasForeignKey(x => x.MatchedPaymentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.StatementBatchId, x.RowNumber }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.TransactionDate, x.Status });
        builder.HasIndex(x => new { x.OrganizationId, x.MatchedPaymentId }).IsUnique()
            .HasFilter("\"MatchedPaymentId\" IS NOT NULL");
    }
}
