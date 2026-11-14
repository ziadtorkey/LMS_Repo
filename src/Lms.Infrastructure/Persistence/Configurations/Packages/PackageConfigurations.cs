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
        builder.Property(x => x.LaunchPath).HasMaxLength(PackageUpload.MaxLaunchPathLength);
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

internal sealed class PackageAttemptConfiguration : IEntityTypeConfiguration<PackageAttempt>
{
    public void Configure(EntityTypeBuilder<PackageAttempt> builder)
    {
        builder.ToTable("PackageAttempts");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Format).HasMaxLength(10).IsRequired();
        builder.Property(x => x.DataJson).HasColumnType("longtext").IsRequired();
        builder.Property(x => x.ScoreRaw).HasPrecision(12, 4);
        builder.Property(x => x.ScoreMin).HasPrecision(12, 4);
        builder.Property(x => x.ScoreMax).HasPrecision(12, 4);
        builder.Property(x => x.StartedAtUtc).HasPrecision(6);
        builder.Property(x => x.LastCommitAtUtc).HasPrecision(6);
        builder.Property(x => x.CompletedAtUtc).HasPrecision(6);
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.Ignore(x => x.IsOpen);
        builder.Ignore(x => x.Model);
        // One open attempt per package and student: OpenMarker is 1 while open and null once completed (nulls never collide).
        builder.HasIndex(x => new { x.PackageId, x.StudentId, x.OpenMarker }).IsUnique();
        builder.HasIndex(x => new { x.ModuleId, x.StudentId });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasOne<PackageUpload>().WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);
    }
}
