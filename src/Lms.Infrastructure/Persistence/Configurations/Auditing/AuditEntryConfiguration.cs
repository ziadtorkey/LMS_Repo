using Lms.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Auditing;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.Property(x => x.EntityType).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Action).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        // longtext (not the json type) so Arabic/emoji text is stored exactly as written, utf8mb4.
        builder.Property(x => x.BeforeJson).HasColumnType("longtext");
        builder.Property(x => x.AfterJson).HasColumnType("longtext");
        builder.HasIndex(x => new { x.OrganizationId, x.TimestampUtc });
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}
