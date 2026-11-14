using Lms.Domain.Attendance;
using Lms.Domain.Classes;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Attendance;

internal static class AttendanceColumns
{
    public const string AsciiPath = "varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci";
}

internal sealed class AttendanceSessionConfiguration : IEntityTypeConfiguration<AttendanceSession>
{
    public void Configure(EntityTypeBuilder<AttendanceSession> builder)
    {
        builder.ToTable("AttendanceSessions");
        builder.Property(x => x.OrgUnitPath).HasColumnType(AttendanceColumns.AsciiPath).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(20).IsRequired();
        builder.Property(x => x.State).HasMaxLength(20).IsRequired();
        builder.Property(x => x.SessionKey).HasMaxLength(100);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => x.SessionKey).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => new { x.ClassCourseId, x.Date });
        builder.HasIndex(x => new { x.ClassId, x.Date });
        builder.HasIndex(x => x.LiveSessionId);
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.ToTable("AttendanceRecords");
        builder.Property(x => x.OrgUnitPath).HasColumnType(AttendanceColumns.AsciiPath).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.Note).HasMaxLength(200);
        builder.Property(x => x.RecordKey).HasMaxLength(70);
        builder.HasIndex(x => x.RecordKey).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => new { x.StudentProfileId, x.Status });
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AttendanceSession>().WithMany().HasForeignKey(x => x.AttendanceSessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AttendanceRecordChangeConfiguration : IEntityTypeConfiguration<AttendanceRecordChange>
{
    public void Configure(EntityTypeBuilder<AttendanceRecordChange> builder)
    {
        builder.ToTable("AttendanceRecordChanges");
        builder.Property(x => x.OrgUnitPath).HasColumnType(AttendanceColumns.AsciiPath).IsRequired();
        builder.Property(x => x.FromStatus).HasMaxLength(20);
        builder.Property(x => x.ToStatus).HasMaxLength(20);
        builder.Property(x => x.Kind).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => new { x.AttendanceRecordId, x.AtUtc });
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AttendanceRecord>().WithMany().HasForeignKey(x => x.AttendanceRecordId).OnDelete(DeleteBehavior.Restrict);
    }
}
