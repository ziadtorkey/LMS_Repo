using Lms.Domain.Content;
using Lms.Domain.Courses;
using Lms.Domain.Library;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence.Configurations.Academic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Library;

internal sealed class LibraryResourceConfiguration : IEntityTypeConfiguration<LibraryResource>
{
    public void Configure(EntityTypeBuilder<LibraryResource> builder)
    {
        builder.ToTable("LibraryResources");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.TitleAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TitleEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DescriptionAr).HasMaxLength(2000);
        builder.Property(x => x.DescriptionEn).HasMaxLength(2000);
        builder.Property(x => x.Type).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Url).HasMaxLength(2000);
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitId, x.Type });
        builder.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Asset>().WithMany().HasForeignKey(x => x.CoverAssetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Asset>().WithMany().HasForeignKey(x => x.FileAssetId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LibraryTagConfiguration : IEntityTypeConfiguration<LibraryTag>
{
    public void Configure(EntityTypeBuilder<LibraryTag> builder)
    {
        builder.ToTable("LibraryTags");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Tag).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.Tag });
        builder.HasIndex(x => x.ResourceId);
        builder.HasOne<LibraryResource>().WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LibraryResourceLevelConfiguration : IEntityTypeConfiguration<LibraryResourceLevel>
{
    public void Configure(EntityTypeBuilder<LibraryResourceLevel> builder)
    {
        builder.ToTable("LibraryResourceLevels");
        AcademicColumns.Unit(builder);
        builder.HasIndex(x => x.ResourceId);
        builder.HasOne<LibraryResource>().WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LevelType>().WithMany().HasForeignKey(x => x.LevelTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}
