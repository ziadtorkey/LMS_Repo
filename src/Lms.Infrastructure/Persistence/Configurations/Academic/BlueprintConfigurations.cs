using Lms.Domain.Blueprints;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Academic;

/// <summary>The scope columns of an academic child row that is not a soft-deletable <c>UnitOwnedEntity</c>.</summary>
internal static class ScopedChild
{
    public static void Map<T>(EntityTypeBuilder<T> builder, System.Linq.Expressions.Expression<Func<T, string>> path,
        System.Linq.Expressions.Expression<Func<T, object?>> unit) where T : class
    {
        builder.Property(path).HasColumnType(AcademicColumns.AsciiPath).IsRequired();
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(unit).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BlueprintConfiguration : IEntityTypeConfiguration<Blueprint>
{
    public void Configure(EntityTypeBuilder<Blueprint> builder)
    {
        builder.ToTable("Blueprints");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ApplyMode).HasMaxLength(30);
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CourseId, x.DraftKey }).IsUnique();
        builder.HasIndex(x => new { x.CourseId, x.VersionKey }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}

internal sealed class BlueprintSectionConfiguration : IEntityTypeConfiguration<BlueprintSection>
{
    public void Configure(EntityTypeBuilder<BlueprintSection> builder)
    {
        builder.ToTable("BlueprintSections");
        ScopedChild.Map(builder, x => x.OrgUnitPath, x => x.OrgUnitId);
        builder.Property(x => x.TitleAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TitleEn).HasMaxLength(200).IsRequired();
        builder.HasOne<Blueprint>().WithMany().HasForeignKey(x => x.BlueprintId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.BlueprintId, x.Key }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}

internal sealed class BlueprintLessonConfiguration : IEntityTypeConfiguration<BlueprintLesson>
{
    public void Configure(EntityTypeBuilder<BlueprintLesson> builder)
    {
        builder.ToTable("BlueprintLessons");
        ScopedChild.Map(builder, x => x.OrgUnitPath, x => x.OrgUnitId);
        builder.Property(x => x.TitleAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TitleEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ObjectivesAr).HasMaxLength(4000);
        builder.Property(x => x.ObjectivesEn).HasMaxLength(4000);
        builder.HasOne<Blueprint>().WithMany().HasForeignKey(x => x.BlueprintId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BlueprintSection>().WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.BlueprintId, x.Key }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}

internal sealed class ClassLessonPlanConfiguration : IEntityTypeConfiguration<ClassLessonPlan>
{
    public void Configure(EntityTypeBuilder<ClassLessonPlan> builder)
    {
        builder.ToTable("ClassLessonPlans");
        AcademicColumns.Unit(builder);
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Blueprint>().WithMany().HasForeignKey(x => x.BlueprintId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ClassCourseKey).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}

internal sealed class ClassSectionConfiguration : IEntityTypeConfiguration<ClassSection>
{
    public void Configure(EntityTypeBuilder<ClassSection> builder)
    {
        builder.ToTable("ClassSections");
        ScopedChild.Map(builder, x => x.OrgUnitPath, x => x.OrgUnitId);
        builder.Property(x => x.TitleAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TitleEn).HasMaxLength(200).IsRequired();
        builder.HasOne<ClassLessonPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.PlanId, x.Key }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}

internal sealed class ClassLessonConfiguration : IEntityTypeConfiguration<ClassLesson>
{
    public void Configure(EntityTypeBuilder<ClassLesson> builder)
    {
        builder.ToTable("ClassLessons");
        ScopedChild.Map(builder, x => x.OrgUnitPath, x => x.OrgUnitId);
        builder.Property(x => x.TitleAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TitleEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ObjectivesAr).HasMaxLength(4000);
        builder.Property(x => x.ObjectivesEn).HasMaxLength(4000);
        builder.HasOne<ClassLessonPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClassSection>().WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.PlanId, x.Key }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}
