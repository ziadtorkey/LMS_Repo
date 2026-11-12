using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Academic;

internal sealed class EnrollmentExceptionConfiguration : IEntityTypeConfiguration<EnrollmentException>
{
    public void Configure(EntityTypeBuilder<EnrollmentException> builder)
    {
        builder.ToTable("EnrollmentExceptions");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.FailedTypesJson).HasColumnType("json").IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.PendingKey).HasMaxLength(80);
        builder.Property(x => x.DecisionComment).HasMaxLength(1000);
        builder.HasIndex(x => x.PendingKey).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath, x.Status });
        builder.HasIndex(x => x.ClassCourseId);
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
    }
}
