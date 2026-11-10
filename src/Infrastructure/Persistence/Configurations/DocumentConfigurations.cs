using Domain.Bills;
using Domain.Documents;
using Domain.Organizations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class SourceDocumentConfiguration : IEntityTypeConfiguration<SourceDocument>
{
    public void Configure(EntityTypeBuilder<SourceDocument> builder)
    {
        builder.ToTable("SourceDocuments");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.FileName).IsRequired().HasMaxLength(200);
        builder.Property(d => d.Kind).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(d => d.Sha256).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.Property(d => d.Status).HasConversion<string>().IsRequired().HasMaxLength(20);
        builder.Property(d => d.RejectionReason).HasMaxLength(500);
        builder.Property(d => d.LastError).HasMaxLength(500);
        builder.Property(d => d.CreatedAt).IsRequired();

        builder.Ignore(d => d.ContentType);

        builder.HasOne<SupplierBill>().WithMany().HasForeignKey(d => d.BillId).OnDelete(DeleteBehavior.Restrict);

        // One document leads to at most one bill.
        builder.HasIndex(d => d.BillId).IsUnique().HasFilter("\"BillId\" IS NOT NULL");

        builder.HasOne<Organization>().WithMany().HasForeignKey(d => d.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);

        // The same file cannot be live twice in one organization. A rejected one does not count.
        builder.HasIndex(d => new { d.OrganizationId, d.Sha256 })
            .IsUnique()
            .HasFilter("\"Status\" <> 'Rejected'");

        builder.HasIndex(d => new { d.OrganizationId, d.Status });
        builder.HasIndex(d => new { d.OrganizationId, d.CreatedAt });
    }
}

public class DocumentBlobConfiguration : IEntityTypeConfiguration<DocumentBlob>
{
    public void Configure(EntityTypeBuilder<DocumentBlob> builder)
    {
        builder.ToTable("DocumentBlobs");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.Content).IsRequired();
        builder.Property(b => b.CreatedAt).IsRequired();

        builder.HasOne<Organization>().WithMany().HasForeignKey(b => b.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SourceDocument>().WithMany().HasForeignKey(b => b.DocumentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => b.DocumentId).IsUnique();
    }
}

public class DocumentExtractionConfiguration : IEntityTypeConfiguration<DocumentExtraction>
{
    public void Configure(EntityTypeBuilder<DocumentExtraction> builder)
    {
        builder.ToTable("DocumentExtractions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Model).IsRequired().HasMaxLength(80);
        builder.Property(e => e.ErrorMessage).HasMaxLength(500);
        builder.Property(e => e.CreatedAt).IsRequired();

        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SourceDocument>().WithMany().HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.DocumentId, e.AttemptNumber });
    }
}