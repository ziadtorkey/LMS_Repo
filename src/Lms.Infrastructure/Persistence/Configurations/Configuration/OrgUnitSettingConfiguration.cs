using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Configuration;

internal sealed class OrgUnitSettingConfiguration : IEntityTypeConfiguration<OrgUnitSetting>
{
    public void Configure(EntityTypeBuilder<OrgUnitSetting> builder)
    {
        builder.ToTable("OrgUnitSettings");
        builder.Property(x => x.OrgUnitPath)
            .HasColumnType("varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci").IsRequired();
        builder.Property(x => x.Key).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ValueJson).HasColumnType("longtext").IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => new { x.OrgUnitId, x.Key }).IsUnique();
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
