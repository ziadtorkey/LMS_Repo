using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence.Configurations.Academic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Content;

internal sealed class LessonModuleConfiguration : IEntityTypeConfiguration<LessonModule>
{
    public void Configure(EntityTypeBuilder<LessonModule> builder)
    {
        builder.ToTable("LessonModules");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TitleAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TitleEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Visibility).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Target).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CompletionRule).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CompletionMinScore).HasPrecision(5, 2);
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClassLesson>().WithMany().HasForeignKey(x => x.ClassLessonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudentGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ClassLessonId, x.Order });
        builder.HasIndex(x => new { x.ClassCourseId, x.LessonKey });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}

internal sealed class ModuleContentConfiguration : IEntityTypeConfiguration<ModuleContent>
{
    public void Configure(EntityTypeBuilder<ModuleContent> builder)
    {
        builder.ToTable("ModuleContents");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.BodyHtmlAr).HasColumnType("longtext");
        builder.Property(x => x.BodyHtmlEn).HasColumnType("longtext");
        builder.Property(x => x.Url).HasMaxLength(2000);
        builder.HasOne<LessonModule>().WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ModuleKey).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}

internal sealed class ModuleCompletionConfiguration : IEntityTypeConfiguration<ModuleCompletion>
{
    public void Configure(EntityTypeBuilder<ModuleCompletion> builder)
    {
        builder.ToTable("ModuleCompletions");
        ScopedChild.Map(builder, x => x.OrgUnitPath, x => x.OrgUnitId);
        builder.Property(x => x.State).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Score).HasPrecision(7, 2);
        builder.HasOne<LessonModule>().WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ModuleId, x.StudentId }).IsUnique();
        builder.HasIndex(x => new { x.ClassCourseId, x.StudentId });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}
