using Lms.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Organizations;

internal sealed class OrgUnitConfiguration : IEntityTypeConfiguration<OrgUnit>
{
    public void Configure(EntityTypeBuilder<OrgUnit> builder)
    {
        builder.ToTable("OrgUnits");
        builder.Ignore(x => x.OrgUnitId);
        builder.Ignore(x => x.OrgUnitPath);
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Path)
            .HasColumnType("varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci")
            .IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.Path });
        builder.HasIndex(x => x.ParentId);
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LevelType>().WithMany().HasForeignKey(x => x.LevelTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}