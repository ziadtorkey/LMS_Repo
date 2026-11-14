using Lms.Domain.Attendance;
using Lms.Domain.Excuses;
using Lms.Domain.Files;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence.Configurations.Academic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Excuses;

internal sealed class AbsenceExcuseConfiguration : IEntityTypeConfiguration<AbsenceExcuse>
{
    public void Configure(EntityTypeBuilder<AbsenceExcuse> builder)
    {
        builder.ToTable("AbsenceExcuses");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Reason).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ReasonText).HasMaxLength(1000);
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DecisionComment).HasMaxLength(500);
        builder.Property(x => x.EscalationComment).HasMaxLength(500);
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.AttachmentFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath, x.Status });
        builder.HasIndex(x => new { x.StudentProfileId, x.Status });
        builder.HasIndex(x => x.SubmittedByUserId);
    }
}

internal sealed class AbsenceExcuseSessionConfiguration : IEntityTypeConfiguration<AbsenceExcuseSession>
{
    public void Configure(EntityTypeBuilder<AbsenceExcuseSession> builder)
    {
        builder.ToTable("AbsenceExcuseSessions");
        AcademicColumns.Unit(builder);
        builder.HasOne<AbsenceExcuse>().WithMany().HasForeignKey(x => x.AbsenceExcuseId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AttendanceSession>().WithMany().HasForeignKey(x => x.AttendanceSessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.AttendanceSessionId);
        builder.HasIndex(x => new { x.AbsenceExcuseId, x.AttendanceSessionId }).IsUnique();
    }
}
