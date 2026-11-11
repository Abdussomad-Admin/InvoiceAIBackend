using Domain.Email;
using Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class EmailOutboxConfiguration : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public void Configure(EntityTypeBuilder<EmailOutboxMessage> builder)
    {
        builder.ToTable("EmailOutbox");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(200);
        builder.Property(x => x.To).IsRequired().HasMaxLength(254);
        builder.Property(x => x.ReplyTo).HasMaxLength(254);
        builder.Property(x => x.Subject).IsRequired().HasMaxLength(500);
        builder.Property(x => x.Body).IsRequired().HasColumnType("text");
        builder.Property(x => x.LastError).HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.AvailableAt, x.ProcessingUntil })
            .HasFilter("\"SentAt\" IS NULL AND NOT \"DeadLettered\"");
    }
}
