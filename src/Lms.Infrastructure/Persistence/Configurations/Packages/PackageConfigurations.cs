using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Files;
using Lms.Domain.Packages;
using Lms.Infrastructure.Persistence.Configurations.Academic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Packages;

internal sealed class PackageUploadConfiguration : IEntityTypeConfiguration<PackageUpload>
{
    public void Configure(EntityTypeBuilder<PackageUpload> builder)
    {
        builder.ToTable("PackageUploads");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Sha256).HasColumnType("char(64) CHARACTER SET ascii").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.RejectReason).HasMaxLength(40);
        builder.Property(x => x.Title).HasMaxLength(500);
        builder.Property(x => x.Version).HasMaxLength(20);
        builder.Property(x => x.MainLibrary).HasMaxLength(200);
        builder.Property(x => x.ExtractedBytes).HasDefaultValue(0L);
        builder.Property(x => x.StorePrefix).HasMaxLength(100).IsRequired();
        builder.Property(x => x.GradingMethod).HasMaxLength(10).IsRequired().HasDefaultValue(PackageUpload.DefaultGradingMethod);
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.ArchiveFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LessonModule>().WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ClassCourseId, x.LessonKey });
        builder.HasIndex(x => x.ModuleId);
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => new { x.OrganizationId, x.Status });
    }
}
