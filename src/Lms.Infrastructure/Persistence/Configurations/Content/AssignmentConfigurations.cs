using Lms.Domain.Assignments;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence.Configurations.Academic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Content;

internal sealed class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> builder)
    {
        builder.ToTable("CourseAssignments");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.InstructionsHtmlAr).HasColumnType("longtext");
        builder.Property(x => x.InstructionsHtmlEn).HasColumnType("longtext");
        builder.Property(x => x.DueAtUtc).HasPrecision(6);
        builder.Property(x => x.MaxGrade).HasPrecision(9, 2);
        builder.Property(x => x.LatePolicy).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ResubmissionRule).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.ClassCourseId, x.DueAtUtc });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AssignmentAttachmentConfiguration : IEntityTypeConfiguration<AssignmentAttachment>
{
    public void Configure(EntityTypeBuilder<AssignmentAttachment> builder)
    {
        builder.ToTable("CourseAssignmentAttachments");
        AcademicColumns.Unit(builder);
        builder.HasIndex(x => new { x.AssignmentId, x.Order });
        builder.HasIndex(x => x.AssetId);
        builder.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.ToTable("Submissions");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TextHtml).HasColumnType("longtext");
        builder.Property(x => x.SubmittedAtUtc).HasPrecision(6);
        builder.HasIndex(x => new { x.AssignmentId, x.StudentKey }).IsUnique();
        builder.HasIndex(x => new { x.ClassCourseId, x.StudentId });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasOne<Assignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SubmissionFileConfiguration : IEntityTypeConfiguration<SubmissionFile>
{
    public void Configure(EntityTypeBuilder<SubmissionFile> builder)
    {
        builder.ToTable("SubmissionFiles");
        AcademicColumns.Unit(builder);
        builder.HasIndex(x => x.SubmissionId);
        builder.HasIndex(x => x.AssetId);
        builder.HasOne<Submission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}
