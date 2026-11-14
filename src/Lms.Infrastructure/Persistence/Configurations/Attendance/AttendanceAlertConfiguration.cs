using Lms.Domain.Attendance;
using Lms.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Attendance;

internal sealed class AttendanceAlertConfiguration : IEntityTypeConfiguration<AttendanceAlert>
{
    public void Configure(EntityTypeBuilder<AttendanceAlert> builder)
    {
        builder.ToTable("AttendanceAlerts");
        builder.Property(x => x.OrgUnitPath).HasColumnType("varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci").IsRequired();
        builder.Property(x => x.Score).HasPrecision(8, 2);
        builder.Property(x => x.Threshold).HasPrecision(8, 2);
        builder.Property(x => x.ActiveKey).HasMaxLength(40);
        builder.HasIndex(x => x.ActiveKey).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => new { x.StudentProfileId, x.AlertedAtUtc });
        builder.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
    }
}
