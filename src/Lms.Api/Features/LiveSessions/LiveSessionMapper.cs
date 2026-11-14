using Lms.Api.Features.Content;
using Lms.Domain.Content;
using Lms.Domain.LiveSessions;
using Lms.Infrastructure.Persistence;
using Lms.Shared.LiveSessions;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.LiveSessions;

/// <summary>Maps live session rows to the contract DTOs. No DTO ever carries the join or the start URL.</summary>
internal sealed class LiveSessionMapper(LmsDbContext db)
{
    public async Task<LiveSessionDto> SessionAsync(
        LiveSession s, LessonModule module, bool canEdit, int joinWindowMinutes, DateTime nowUtc, CancellationToken ct)
    {
        var groups = await ModuleMapper.GroupsAsync(db, [module], ct);
        var node = ModuleMapper.Node(module, nowUtc, groups, null);
        LiveRecurrenceDto? recurrence = null;
        int? count = null;
        if (s.SeriesId is { } seriesId)
        {
            var series = await db.Set<LiveSessionSeries>().FirstAsync(x => x.Id == seriesId, ct);
            recurrence = new LiveRecurrenceDto(series.IntervalWeeks, series.Days, ModuleMapper.Utc(series.UntilUtc), series.Count, series.TimeZoneId);
            count = await db.Set<LiveSession>().CountAsync(x => x.SeriesId == seriesId, ct);
        }

        return new LiveSessionDto(
            s.Id, s.ClassCourseId, s.LessonKey, s.SeriesId, s.OccurrenceIndex, count, node, Utc(s.StartsAtUtc), Utc(s.EndsAtUtc),
            s.DurationMinutes, s.Status, recurrence, s.RecordingUrl, s.RecordingSource, Utc(s.StartsAtUtc.AddMinutes(-joinWindowMinutes)),
            canEdit, nowUtc);
    }

    public static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
