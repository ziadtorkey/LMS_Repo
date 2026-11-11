using Lms.Domain.AcademicYears;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Identity;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Academic;

internal sealed class ClassConfiguration : IEntityTypeConfiguration<Class>
{
    public void Configure(EntityTypeBuilder<Class> builder)
    {
        builder.ToTable("Classes");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CodeKey).HasMaxLength(50);
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.AcademicYearId, x.CodeKey }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath, x.AcademicYearId });
        builder.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ClassStudentConfiguration : IEntityTypeConfiguration<ClassStudent>
{
    public void Configure(EntityTypeBuilder<ClassStudent> builder)
    {
        builder.ToTable("ClassStudents");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.From).HasColumnType("date");
        builder.Property(x => x.To).HasColumnType("date");
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.EndReason).HasMaxLength(500);
        builder.HasIndex(x => new { x.OrganizationId, x.StudentId, x.ActiveYearKey }).IsUnique();
        builder.HasIndex(x => new { x.StudentId, x.Status });
        builder.HasIndex(x => new { x.ClassId, x.Status });
        builder.HasOne<Class>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ClassCourseConfiguration : IEntityTypeConfiguration<ClassCourse>
{
    public void Configure(EntityTypeBuilder<ClassCourse> builder)
    {
        builder.ToTable("ClassCourses");
        AcademicColumns.Unit(builder);
        builder.HasIndex(x => new { x.ClassId, x.CourseKey }).IsUnique();
        builder.HasIndex(x => x.CourseId);
        builder.HasOne<Class>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TeacherCourseEligibilityConfiguration : IEntityTypeConfiguration<TeacherCourseEligibility>
{
    public void Configure(EntityTypeBuilder<TeacherCourseEligibility> builder)
    {
        builder.ToTable("TeacherCourseEligibilities");
        AcademicColumns.Unit(builder);
        builder.HasIndex(x => new { x.OrgUnitId, x.UserId, x.CourseId, x.LiveKey }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.UserId });
        builder.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ClassCourseTeacherConfiguration : IEntityTypeConfiguration<ClassCourseTeacher>
{
    public void Configure(EntityTypeBuilder<ClassCourseTeacher> builder)
    {
        builder.ToTable("ClassCourseTeachers");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Role).HasMaxLength(20).IsRequired();
        builder.Property(x => x.From).HasColumnType("date");
        builder.Property(x => x.To).HasColumnType("date");
        builder.HasIndex(x => new { x.ClassCourseId, x.OpenPrimaryKey }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.UserId });
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CourseEnrollmentConfiguration : IEntityTypeConfiguration<CourseEnrollment>
{
    public void Configure(EntityTypeBuilder<CourseEnrollment> builder)
    {
        builder.ToTable("CourseEnrollments");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Source).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DropReason).HasMaxLength(500);
        builder.Property(x => x.From).HasColumnType("date");
        builder.Property(x => x.To).HasColumnType("date");
        builder.HasIndex(x => new { x.ClassCourseId, x.StudentId, x.ActiveKey }).IsUnique();
        builder.HasIndex(x => new { x.StudentId, x.Status });
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClassStudent>().WithMany().HasForeignKey(x => x.ClassStudentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StudentGroupConfiguration : IEntityTypeConfiguration<StudentGroup>
{
    public void Configure(EntityTypeBuilder<StudentGroup> builder)
    {
        builder.ToTable("StudentGroups");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.MatchType).HasMaxLength(30);
        builder.Property(x => x.MatchValue).HasMaxLength(100);
        builder.HasIndex(x => x.ClassCourseId);
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.ToTable("GroupMembers");
        AcademicColumns.Unit(builder);
        // D6: at most one live group membership per student and class course, enforced by the database.
        builder.HasIndex(x => new { x.ClassCourseId, x.StudentId, x.LiveKey }).IsUnique();
        builder.HasIndex(x => x.GroupId);
        builder.HasOne<StudentGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CourseEnrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StudentTransferConfiguration : IEntityTypeConfiguration<StudentTransfer>
{
    public void Configure(EntityTypeBuilder<StudentTransfer> builder)
    {
        builder.ToTable("StudentTransfers");
        builder.Property(x => x.OrgUnitPath).HasColumnType(AcademicColumns.AsciiPath).IsRequired();
        builder.Property(x => x.FromOrgUnitPath).HasColumnType(AcademicColumns.AsciiPath).IsRequired();
        builder.Property(x => x.EffectiveOn).HasColumnType("date");
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => new { x.StudentId, x.EffectiveOn });
        builder.HasOne<Lms.Domain.Organizations.OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Class>().WithMany().HasForeignKey(x => x.FromClassId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Class>().WithMany().HasForeignKey(x => x.ToClassId).OnDelete(DeleteBehavior.Restrict);
    }
}
