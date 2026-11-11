using Lms.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Organizations;

internal sealed class OrgTemplateConfiguration : IEntityTypeConfiguration<OrgTemplate>
{
    public void Configure(EntityTypeBuilder<OrgTemplate> builder)
    {
        builder.ToTable("OrgTemplates");
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.OrgUnitPath)
            .HasColumnType("varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci")
            .IsRequired();
        builder.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Placeholders).HasMaxLength(1000).IsRequired();
        // longtext so Arabic names inside the snapshot are stored exactly as written (utf8mb4).
        builder.Property(x => x.SnapshotJson).HasColumnType("longtext").IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
