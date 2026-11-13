using Lms.Domain.Classes;
using Lms.Domain.LiveSessions;
using Lms.Infrastructure.Persistence.Configurations.Academic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lms.Infrastructure.Persistence.Configurations.Content;

internal sealed class LiveSessionSeriesConfiguration : IEntityTypeConfiguration<LiveSessionSeries>
{
    public void Configure(EntityTypeBuilder<LiveSessionSeries> builder)
    {
        builder.ToTable("LiveSessionSeries");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.DaysOfWeek).HasMaxLength(80).IsRequired();
        builder.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.UntilUtc).HasPrecision(6);
        builder.Ignore(x => x.Days);
        builder.Ignore(x => x.Rule);
        builder.HasIndex(x => new { x.ClassCourseId, x.LessonKey });
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LiveSessionConfiguration : IEntityTypeConfiguration<LiveSession>
{
    public void Configure(EntityTypeBuilder<LiveSession> builder)
    {
        builder.ToTable("LiveSessions");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.Provider).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ExternalMeetingId).HasMaxLength(64);
        builder.Property(x => x.HostUserKey).HasMaxLength(100);
        builder.Property(x => x.JoinUrlProtected).HasColumnType("text");
        builder.Property(x => x.StartUrlProtected).HasColumnType("text");
        builder.Property(x => x.RecordingUrl).HasMaxLength(2048);
        builder.Property(x => x.RecordingSource).HasMaxLength(20);
        builder.Property(x => x.StartsAtUtc).HasPrecision(6);
        builder.Property(x => x.ReminderSentAtUtc).HasPrecision(6);
        builder.Property(x => x.ParticipantsImportedAtUtc).HasPrecision(6);
        builder.Ignore(x => x.EndsAtUtc);
        builder.HasIndex(x => new { x.ClassCourseId, x.StartsAtUtc });
        builder.HasIndex(x => x.ExternalMeetingId);
        builder.HasIndex(x => x.SeriesId);
        builder.HasIndex(x => x.ModuleId);
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasOne<ClassCourse>().WithMany().HasForeignKey(x => x.ClassCourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LiveSessionSeries>().WithMany().HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LiveSessionParticipantConfiguration : IEntityTypeConfiguration<LiveSessionParticipant>
{
    public void Configure(EntityTypeBuilder<LiveSessionParticipant> builder)
    {
        builder.ToTable("LiveSessionParticipants");
        AcademicColumns.Unit(builder);
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.MatchState).HasMaxLength(20).IsRequired();
        builder.Property(x => x.JoinedAtUtc).HasPrecision(6);
        builder.Property(x => x.LeftAtUtc).HasPrecision(6);
        builder.HasIndex(x => new { x.SessionId, x.JoinedAtUtc });
        builder.HasIndex(x => x.StudentId);
        builder.HasIndex(x => new { x.OrganizationId, x.OrgUnitPath });
        builder.HasOne<LiveSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
    }
}
