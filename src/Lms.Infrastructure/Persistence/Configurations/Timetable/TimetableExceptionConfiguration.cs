using Lms.Domain.Organizations;
using Lms.Domain.Timetable;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Timetable;

internal sealed class TimetableExceptionConfiguration : IEntityTypeConfiguration<TimetableException>
{
    public void Configure(EntityTypeBuilder<TimetableException> builder)
    {
        builder.ToTable("TimetableExceptions");
        builder.Property(x => x.OrgUnitPath)
            .HasColumnType("varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci")
            .IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.ExceptionKey).HasMaxLength(60);
        builder.HasIndex(x => x.ExceptionKey).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => new { x.SlotId, x.Date });
        builder.HasIndex(x => new { x.SubstituteUserId, x.Date });
        builder.HasOne<TimetableSlot>().WithMany().HasForeignKey(x => x.SlotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Period>().WithMany().HasForeignKey(x => x.NewPeriodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
