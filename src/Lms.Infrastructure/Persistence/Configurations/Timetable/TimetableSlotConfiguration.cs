using Lms.Domain.Classes;
using Lms.Domain.Timetable;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Timetable;

internal sealed class TimetableSlotConfiguration : IEntityTypeConfiguration<TimetableSlot>
{
    public void Configure(EntityTypeBuilder<TimetableSlot> builder)
    {
        builder.ToTable("TimetableSlots");
        builder.Property(x => x.OrgUnitPath)
            .HasColumnType("varchar(400) CHARACTER SET ascii COLLATE ascii_general_ci")
            .IsRequired();
        builder.Property(x => x.RoomName).HasMaxLength(100);
        builder.Property(x => x.State).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Day).HasConversion<int>();
        builder.Property(x => x.SlotKey).HasMaxLength(120);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => x.SlotKey).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasIndex(x => new { x.ClassId, x.TermId });
        builder.HasIndex(x => new { x.TeacherUserId, x.TermId, x.Day, x.PeriodId });
        builder.HasOne<Class>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Period>().WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Restrict);
    }
}
