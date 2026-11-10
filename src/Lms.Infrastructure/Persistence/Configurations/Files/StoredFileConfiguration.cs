using Lms.Domain.Files;
using Lms.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Files;

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("StoredFiles");
        builder.Property(x => x.OrgUnitPath)
            .HasColumnType("varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci").IsRequired();
        builder.Property(x => x.ObjectKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => x.OrgUnitId);
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
