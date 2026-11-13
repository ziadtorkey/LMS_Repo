using Lms.Api.Features.Content;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Courses;
using Lms.Domain.LiveSessions;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.LiveSessions;

/// <summary>The staff participants read and the caller's upcoming sessions (M4b Task 6, FR-VC-01).</summary>
public sealed class LiveSessionReadHandlers(
    LmsDbContext db, ModuleAccess access, IRelationshipScope scope, ISettingsResolver settings, IClock clock)
{
    private const int Candidates = 100;

    public async Task<IResult> ParticipantsAsync(Guid id, LiveParticipantQuery query, HttpContext http, CancellationToken ct)
    {
        var session = await db.Set<LiveSession>().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null)
        {
            return ApiProblems.NotFound();
        }

        var (_, problem) = await http.CheckClassCourseAsync(Permissions.LiveSessions.Manage, session.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var rows = from p in db.Set<LiveSessionParticipant>()
                   join s in db.Set<StudentProfile>() on p.StudentId equals s.Id into students
                   from student in students.DefaultIfEmpty()
                   where p.SessionId == id
                   select new { P = p, student.NameAr, student.NameEn };
        if (!string.IsNullOrWhiteSpace(query.MatchState))
        {
            rows = rows.Where(r => r.P.MatchState == query.MatchState);
        }

        var total = await rows.CountAsync(ct);
        var size = Math.Clamp(query.PageSize, 1, Paging.MaxPageSize);
        var number = Math.Max(query.Page, 1);
        var page = await rows.OrderBy(r => r.P.JoinedAtUtc).ThenBy(r => r.P.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
        var items = page.Select(r => new LiveParticipantDto(
            r.P.Id, r.P.DisplayName, r.P.MatchState, r.P.StudentId, r.NameAr, r.NameEn, LiveSessionMapper.Utc(r.P.JoinedAtUtc),
            r.P.LeftAtUtc is { } left ? LiveSessionMapper.Utc(left) : null, (int)Math.Round(r.P.DurationSeconds / 60.0))).ToList();
        return Results.Ok(new PagedResult<LiveParticipantDto>(items, total, number, size));
    }

    /// <summary>
    /// The caller's next sessions that have not ended, at most five: a student's own enrollments (visible modules only), a teacher's
    /// own class courses, other staff their unit scope. The filters of the data layer apply; this never answers 404.
    /// </summary>
    public async Task<IResult> UpcomingAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var filter = await scope.ForAsync(Permissions.LiveSessions.View, ct);
        // A session that started more than the longest duration ago has ended; the exact end is checked in memory.
        var earliest = now.AddMinutes(-LiveSessionLimits.DurationMaxMinutes);
        var open = db.Set<LiveSession>().Where(
            s => (s.Status == LiveSessionRules.Scheduled || s.Status == LiveSessionRules.Live) && s.StartsAtUtc >= earliest);
        if (!filter.IsUnrestricted)
        {
            var ids = filter.ClassCourseIds.ToList();
            open = open.Where(s => ids.Contains(s.ClassCourseId));
        }

        var candidates = (await open.OrderBy(s => s.StartsAtUtc).Take(Candidates * 2).ToListAsync(ct))
            .Where(s => s.EndsAtUtc > now).Take(Candidates).ToList();
        if (candidates.Count == 0)
        {
            return Results.Ok(Array.Empty<UpcomingLiveSessionDto>());
        }

        var ccIds = candidates.Select(s => s.ClassCourseId).Distinct().ToList();
        var modules = await db.Set<LessonModule>()
            .Where(m => m.Kind == ModuleKinds.LiveSession && candidates.Select(s => s.Id).Contains(m.RefId!.Value)).ToListAsync(ct);
        var byRef = modules.ToDictionary(m => m.RefId!.Value);

        HashSet<Guid>? visible = null;
        if (await access.IsStudentAsync(ct))
        {
            visible = [];
            if (await access.StudentIdAsync(ct) is { } studentId)
            {
                foreach (var ccId in ccIds)
                {
                    var group = await access.GroupOfAsync(ccId, studentId, ct);
                    visible.UnionWith(await access.VisibleTo(ccId, group, now).Select(p => p.Module.Id).ToListAsync(ct));
                }
            }
        }

        var shown = candidates
            .Where(s => byRef.TryGetValue(s.Id, out var m) && (visible is null || visible.Contains(m.Id)))
            .Take(LiveSessionLimits.UpcomingMax).ToList();
        var courses = await (from cc in db.Set<ClassCourse>()
                             join c in db.Set<Course>() on cc.CourseId equals c.Id
                             where ccIds.Contains(cc.Id)
                             select new { cc.Id, c.NameAr, c.NameEn }).ToDictionaryAsync(x => x.Id, ct);
        var window = 0;
        var items = new List<UpcomingLiveSessionDto>();
        foreach (var s in shown)
        {
            var m = byRef[s.Id];
            var course = courses.GetValueOrDefault(s.ClassCourseId);
            var path = s.OrgUnitPath;
            window = (await settings.ResolveAsync(SettingKeys.LiveJoinWindowMinutes, path, ct)).Value;
            items.Add(new UpcomingLiveSessionDto(
                s.Id, m.Id, s.ClassCourseId, course?.NameAr ?? string.Empty, course?.NameEn ?? string.Empty, m.TitleAr, m.TitleEn,
                LiveSessionMapper.Utc(s.StartsAtUtc), LiveSessionMapper.Utc(s.EndsAtUtc), s.Status,
                LiveSessionMapper.Utc(s.StartsAtUtc.AddMinutes(-window)), now));
        }

        return Results.Ok(items);
    }
}
