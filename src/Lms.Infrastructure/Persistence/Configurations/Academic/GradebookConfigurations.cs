using Lms.Domain.AcademicYears;
using Lms.Domain.Classes;
using Lms.Domain.Grading;
using Lms.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Academic;

internal sealed class GradebookConfiguration : IEntityTypeConfiguration<Gradebook>
{
    public void Configure(EntityTypeBuilder<Gradebook> builder)
    {
        builder.ToTable("Gradebooks");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.CategoryMethod).HasMaxLength(20).IsRequired();
        builder.Property(x => x.MissingPolicy).HasMaxLength(20).IsRequired();
        builder.Property(x => x.RoundingMode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.State).HasMaxLength(10).IsRequired();
        builder.Property(x => x.LockedAtUtc).HasPrecision(6);
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GradingScale>().WithMany().HasForeignKey(x => x.GradingScaleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ClassCourseKey, x.TermId }).IsUnique();
        builder.HasIndex(x => x.TermId);
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}

internal sealed class GradeCategoryConfiguration : IEntityTypeConfiguration<GradeCategory>
{
    public void Configure(EntityTypeBuilder<GradeCategory> builder)
    {
        builder.ToTable("GradeCategories");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.NameAr).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Weight).HasPrecision(5, 2);
        builder.HasOne<Gradebook>().WithMany().HasForeignKey(x => x.GradebookId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.UncategorisedKey).IsUnique();
        builder.HasIndex(x => new { x.GradebookId, x.Order });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}

internal sealed class GradeItemConfiguration : IEntityTypeConfiguration<GradeItem>
{
    public void Configure(EntityTypeBuilder<GradeItem> builder)
    {
        builder.ToTable("GradeItems");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.TitleAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TitleEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.MaxPoints).HasPrecision(9, 2);
        builder.Property(x => x.PublishState).HasMaxLength(10).IsRequired();
        builder.Property(x => x.PublishAtUtc).HasPrecision(6);
        builder.Property(x => x.PublishedAtUtc).HasPrecision(6);
        builder.HasOne<Gradebook>().WithMany().HasForeignKey(x => x.GradebookId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GradeCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.GradebookId, x.SourceKind, x.SourceKey }).IsUnique();
        builder.HasIndex(x => new { x.GradebookId, x.PublishState });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}

internal sealed class GradeEntryConfiguration : IEntityTypeConfiguration<GradeEntry>
{
    public void Configure(EntityTypeBuilder<GradeEntry> builder)
    {
        builder.ToTable("GradeEntries");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Points).HasPrecision(9, 2);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.AtUtc).HasPrecision(6);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasOne<Gradebook>().WithMany().HasForeignKey(x => x.GradebookId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GradeItem>().WithMany().HasForeignKey(x => x.GradeItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ItemKey, x.StudentProfileId }).IsUnique();
        builder.HasIndex(x => new { x.GradebookId, x.StudentProfileId });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
    }
}
