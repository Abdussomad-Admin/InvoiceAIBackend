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
        builder.Property(d => d.CreatedAt).IsRequired();

        builder.Ignore(d => d.ContentType);

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