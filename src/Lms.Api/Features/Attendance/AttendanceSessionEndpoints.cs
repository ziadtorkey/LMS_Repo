using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Attendance;
using Lms.Domain.Calendar;
using Lms.Domain.Classes;
using Lms.Domain.Teaching;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Attendance;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Infrastructure.Timetable;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Attendance;

/// <summary>
/// Attendance sessions of a teacher (FR-ATT-01, M5 Task 16): the day list, with the sessions one substitutes on, and the ad hoc session of a ClassCourse.
/// Taking attendance is Task 17. Dates are the client-local dates of <see cref="IClientCalendar"/>.
/// </summary>
public static class AttendanceSessionEndpoints
{
    public static IEndpointRouteBuilder MapAttendanceSessions(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.AttendanceSessionsPattern, ListAsync).RequirePermission(Permissions.Attendance.Take);
        app.MapPost(Routes.ClassCourseAttendanceSessionsPattern, CreateAdHocAsync).RequirePermission(Permissions.Attendance.Take);
        return app;
    }

    private static async Task<IResult> ListAsync(
        [AsParameters] AttendanceSessionsQuery query, LmsDbContext db, ICurrentContext current, IClientCalendar calendar, AttendanceListMapper mapper, CancellationToken ct)
    {
        if (current.UserId is not { } me || current.OrganizationId is not { } organizationId)
        {
            return ApiProblems.NotFound();
        }

        var today = await calendar.LocalTodayAsync(organizationId, ct);
        var date = query.Date ?? today;
        var taught = db.Set<ClassCourseTeacher>().Where(t => t.UserId == me && t.From <= date && (t.To == null || t.To >= date)).Select(t => t.ClassCourseId);
        var substituteSlots = db.Set<TimetableException>()
            .Where(e => e.Date == date && e.Kind == TimetableException.SubstituteKind && e.SubstituteUserId == me).Select(e => e.SlotId);
        // A teacher substituted away for the date does not keep the session: the substitute is the teacher of record for that one session (L15).
        var substitutedAway = db.Set<TimetableException>()
            .Where(e => e.Date == date && e.Kind == TimetableException.SubstituteKind && e.SubstituteUserId != me).Select(e => e.SlotId);
        var sessions = await db.Set<AttendanceSession>()
            .Where(s => s.Date == date && ((taught.Contains(s.ClassCourseId) && !(s.SlotId != null && substitutedAway.Contains(s.SlotId.Value)))
                                           || (s.SlotId != null && substituteSlots.Contains(s.SlotId.Value))))
            .OrderBy(s => s.PeriodId).ThenBy(s => s.Id).ToListAsync(ct);
        return Results.Ok(await mapper.ToDtosAsync(sessions, me, today, ct));
    }

    private static async Task<IResult> CreateAdHocAsync(
        Guid id, CreateAdHocAttendanceSessionRequest? body, HttpContext http, LmsDbContext db, ICurrentContext current, IClientCalendar calendar,
        PlacementGuard guard, PeriodService periods, AttendanceListMapper mapper, CancellationToken ct)
    {
        var (classCourse, problem) = await http.CheckClassCourseAsync(Permissions.Attendance.Take, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (body is null || current.UserId is not { } me)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "body: required");
        }

        var today = await calendar.LocalTodayAsync(classCourse!.OrganizationId, ct);
        if (body.Date > today)
        {
            return ApiProblems.Conflict(ProblemCodes.attendance_session_future);
        }

        // A school day (working, not a holiday) no further back than the report window: not three years ago, not on a holiday.
        if (body.Date < today.AddDays(-AttendanceLimits.SearchMaxDays)
            || !await calendar.IsWorkingDayAsync(classCourse.OrgUnitId, body.Date, ct) || await calendar.IsHolidayAsync(classCourse.OrgUnitId, body.Date, ct))
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "date: not_a_school_day_in_range");
        }

        var cls = await db.Set<Class>().SingleAsync(c => c.Id == classCourse.ClassId, ct);
        try
        {
            await guard.EnsureWritableAsync(cls, ct);
        }
        catch (Lms.Domain.Enrollment.EnrollmentRuleException rule)
        {
            return ApiProblems.Conflict(rule.Code);
        }

        if (body.PeriodId is { } periodId)
        {
            var unit = await db.OrgUnits.SingleAsync(u => u.Id == classCourse.OrgUnitId, ct);
            var (effective, _) = await periods.EffectiveAsync(unit, ct);
            if (effective.All(p => p.Id != periodId))
            {
                return ApiProblems.BadRequest(ProblemCodes.validation_failed, "periodId: unknown");
            }
        }

        // One session per class course, date and period: a timetable session already there is returned (no second roster), and so is an ad hoc one
        // (the unique key also answers a double click: the second request finds the first).
        var existing = await FindAsync(db, id, body, ct);
        var session = existing ?? new AttendanceSession(classCourse, body.Date, null, body.PeriodId, AttendanceSession.AdHocSource);
        if (existing is null)
        {
            db.Add(session);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (AttendanceSessionGenerator.IsDuplicate(ex))
            {
                db.ChangeTracker.Clear();
                existing = await FindAsync(db, id, body, ct);
                if (existing is null)
                {
                    throw;
                }

                session = existing;
            }
        }

        var dto = (await mapper.ToDtosAsync([session], me, today, ct))[0];
        return existing is null ? Results.Created($"/api/attendance-sessions/{session.Id}/records", dto) : Results.Ok(dto);
    }

    private static Task<AttendanceSession?> FindAsync(LmsDbContext db, Guid classCourseId, CreateAdHocAttendanceSessionRequest body, CancellationToken ct) =>
        db.Set<AttendanceSession>().FirstOrDefaultAsync(
            s => s.ClassCourseId == classCourseId && s.Date == body.Date && s.PeriodId == body.PeriodId
                 && (s.Source == AttendanceSession.AdHocSource || s.Source == AttendanceSession.TimetableSource), ct);
}

/// <summary>Builds <see cref="AttendanceSessionListItemDto"/> rows: names, period times, counts, the edit window end and the substitute flag.</summary>
public sealed class AttendanceListMapper(
    LmsDbContext db, AttendanceRoster roster, ExceptionLookups lookups, PeriodService periods, ISettingsResolver settings, IClock clock)
{
    public async Task<IReadOnlyList<AttendanceSessionListItemDto>> ToDtosAsync(IReadOnlyList<AttendanceSession> sessions, Guid callerUserId, DateOnly today, CancellationToken ct)
    {
        if (sessions.Count == 0)
        {
            return [];
        }

        var organizationId = sessions[0].OrganizationId;
        var classCourseIds = sessions.Select(s => s.ClassCourseId).Distinct().ToList();
        var classCourses = await db.Set<ClassCourse>().Where(c => classCourseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var courses = await lookups.CoursesAsync(organizationId, classCourses.Values.Select(c => c.CourseId).Distinct().ToList(), ct);
        var classIds = sessions.Select(s => s.ClassId).Distinct().ToList();
        var classes = await db.Set<Class>().Where(c => classIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var counts = await roster.CountsAsync(sessions.Select(s => (s.ClassCourseId, s.Date)).ToList(), ct);
        var sessionIds = sessions.Select(s => s.Id).ToList();
        var marked = (await db.Set<AttendanceRecord>().Where(r => sessionIds.Contains(r.AttendanceSessionId) && r.Status != null)
                .GroupBy(r => r.AttendanceSessionId).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.N);
        var slotIds = sessions.Where(s => s.SlotId != null).Select(s => s.SlotId!.Value).Distinct().ToList();
        var substitutions = await db.Set<TimetableException>()
            .Where(e => slotIds.Contains(e.SlotId) && e.Kind == TimetableException.SubstituteKind && e.SubstituteUserId == callerUserId)
            .Select(e => new { e.SlotId, e.Date }).ToListAsync(ct);
        var periodTimes = new Dictionary<Guid, (TimeOnly Start, TimeOnly End)>();
        foreach (var unitGroup in sessions.GroupBy(s => s.OrgUnitId))
        {
            var unit = await db.OrgUnits.SingleAsync(u => u.Id == unitGroup.Key, ct);
            foreach (var period in (await periods.EffectiveAsync(unit, ct)).Periods)
            {
                periodTimes[period.Id] = (period.StartTime, period.EndTime);
            }
        }

        var windowByPath = new Dictionary<string, int>();
        var items = new List<AttendanceSessionListItemDto>();
        foreach (var s in sessions)
        {
            var course = classCourses.GetValueOrDefault(s.ClassCourseId) is { } cc ? courses.GetValueOrDefault(cc.CourseId) : null;
            var cls = classes.GetValueOrDefault(s.ClassId);
            if (!windowByPath.TryGetValue(s.OrgUnitPath, out var hours))
            {
                hours = (await settings.ResolveAsync(SettingKeys.AttendanceEditWindowHours, s.OrgUnitPath, ct)).Value;
                windowByPath[s.OrgUnitPath] = hours;
            }

            var times = s.PeriodId is { } p && periodTimes.TryGetValue(p, out var t) ? t : ((TimeOnly, TimeOnly)?)null;
            items.Add(new AttendanceSessionListItemDto(
                s.Id, s.ClassCourseId, course?.NameAr ?? string.Empty, course?.NameEn ?? string.Empty, cls?.NameAr ?? string.Empty, cls?.NameEn ?? string.Empty,
                s.Date, s.PeriodId, times?.Item1, times?.Item2, s.Source, s.State,
                marked.GetValueOrDefault(s.Id), counts.GetValueOrDefault((s.ClassCourseId, s.Date)),
                s.TakenAtUtc?.AddHours(hours), s.SlotId is { } slot && substitutions.Any(x => x.SlotId == slot && x.Date == s.Date),
                s.Source == AttendanceSession.LiveSource && s.State == AttendanceSession.OpenState, s.Version, clock.UtcNow));
        }

        return items;
    }
}
