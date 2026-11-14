using Lms.Domain.Organizations;
using Lms.Domain.Timetable;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Timetable;

internal sealed class PeriodConfiguration : IEntityTypeConfiguration<Period>
{
    public void Configure(EntityTypeBuilder<Period> builder)
    {
        builder.ToTable("Periods");
        builder.Property(x => x.NameAr).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(100).IsRequired();
        builder.Property(x => x.OrgUnitPath)
            .HasColumnType("varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci")
            .IsRequired();
        // Local wall-clock time. The MySQL data reader hands back TimeSpan for `time` columns, so TimeOnly is converted explicitly.
        builder.Property(x => x.StartTime).HasConversion(t => t.ToTimeSpan(), t => TimeOnly.FromTimeSpan(t)).HasColumnType("time(0)");
        builder.Property(x => x.EndTime).HasConversion(t => t.ToTimeSpan(), t => TimeOnly.FromTimeSpan(t)).HasColumnType("time(0)");
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => new { x.OrgUnitId, x.StartTime });
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
