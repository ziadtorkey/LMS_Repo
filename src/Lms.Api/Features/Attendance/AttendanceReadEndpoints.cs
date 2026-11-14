using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Attendance;
using Lms.Domain.Calendar;
using Lms.Domain.Classes;
using Lms.Domain.People;
using Lms.Infrastructure.Attendance;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Attendance;

/// <summary>
/// The read side of attendance (FR-ATT-01, M5 Task 17): the unit and date session search (Attendance.View), summaries and reports, and the own and guardian reads
/// (Attendance.ViewOwn). Reads never write. Dates are the client-local dates of <see cref="IClientCalendar"/>.
/// </summary>
public static class AttendanceReadEndpoints
{
    private const int DefaultRangeDays = 30;

    public static IEndpointRouteBuilder MapAttendanceReads(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.OrgUnitAttendanceSessionsPattern, SearchAsync).RequirePermission(Permissions.Attendance.View);
        app.MapGet(Routes.AttendanceReportPattern, ReportAsync).RequirePermission(Permissions.Attendance.View);
        app.MapGet(Routes.MeAttendance, MineAsync).RequirePermission(Permissions.Attendance.ViewOwn);
        app.MapGet(Routes.StudentAttendancePattern, ChildAsync).RequirePermission(Permissions.Attendance.ViewOwn);
        return app;
    }

    private static async Task<IResult> SearchAsync(
        Guid id, [AsParameters] AttendanceSessionSearchQuery query, HttpContext http, LmsDbContext db, ICurrentContext current, IClientCalendar calendar,
        AttendanceListMapper mapper, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Attendance.View, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        if (query.To < query.From || query.To.DayNumber - query.From.DayNumber > AttendanceLimits.SearchMaxDays
            || (query.State is not null && !AttendanceSessionStates.All.Contains(query.State)))
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, $"The range is at most {AttendanceLimits.SearchMaxDays} days.");
        }

        var unit = access.Unit!;
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);
        var sessions = db.Set<AttendanceSession>().Where(s => s.OrgUnitPath.StartsWith(unit.Path) && s.Date >= query.From && s.Date <= query.To);
        if (query.ClassId is { } classId)
        {
            sessions = sessions.Where(s => s.ClassId == classId);
        }

        if (query.ClassCourseId is { } classCourseId)
        {
            sessions = sessions.Where(s => s.ClassCourseId == classCourseId);
        }

        if (query.State is { } state)
        {
            sessions = sessions.Where(s => s.State == state);
        }

        var total = await sessions.CountAsync(ct);
        var rows = await sessions.OrderByDescending(s => s.Date).ThenBy(s => s.PeriodId).ThenBy(s => s.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var today = await calendar.LocalTodayAsync(unit.OrganizationId, ct);
        return Results.Ok(new PagedResult<AttendanceSessionListItemDto>(await mapper.ToDtosAsync(rows, current.UserId ?? Guid.Empty, today, ct), total, page, size));
    }

    private static async Task<IResult> ReportAsync(
        string kind, [AsParameters] AttendanceReportQuery query, HttpContext http, LmsDbContext db, ICurrentContext current, IClientCalendar calendar, IClock clock,
        ExceptionLookups lookups, CancellationToken ct)
    {
        if (!AttendanceReportKinds.All.Contains(kind) || current.OrganizationId is not { } organizationId)
        {
            return ApiProblems.NotFound();
        }

        var today = await calendar.LocalTodayAsync(organizationId, ct);
        var to = query.To ?? today;
        var start = query.From ?? to.AddDays(-DefaultRangeDays);
        if (to < start || to.DayNumber - start.DayNumber > 366)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "The range is at most 366 days.");
        }

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);
        var records = from r in db.Set<AttendanceRecord>()
                      join s in db.Set<AttendanceSession>() on r.AttendanceSessionId equals s.Id
                      where s.State == AttendanceSession.TakenState && s.Date >= start && s.Date <= to && r.Status != null
                      select new { r.StudentProfileId, s.ClassCourseId, s.ClassId, r.Status };
        List<AttendanceReportRowDto> rows;
        if (kind == AttendanceReportKinds.Student)
        {
            if (query.StudentId is not { } studentId)
            {
                return ApiProblems.BadRequest(ProblemCodes.validation_failed, "studentId is required.");
            }

            var (student, problem) = await http.CheckStudentAsync(Permissions.Attendance.View, studentId, ct);
            if (student is null)
            {
                return problem!;
            }

            var mine = await records.Where(r => r.StudentProfileId == studentId).ToListAsync(ct);
            var ccIds = mine.Select(r => r.ClassCourseId).Distinct().ToList();
            var names = await CourseNamesAsync(db, lookups, organizationId, ccIds, ct);
            rows = mine.GroupBy(r => r.ClassCourseId).Select(g => new AttendanceReportRowDto(
                student.Id, student.NameAr, student.NameEn, student.StudentCode, g.Key, names.GetValueOrDefault(g.Key).Ar, names.GetValueOrDefault(g.Key).En,
                AttendanceSummaries.Summarize(g.Select(x => x.Status)))).OrderBy(r => r.CourseNameEn).ToList();
        }
        else
        {
            if (query.ClassId is not { } classId)
            {
                return ApiProblems.BadRequest(ProblemCodes.validation_failed, "classId is required.");
            }

            var (cls, problem) = await http.CheckClassAsync(Permissions.Attendance.View, classId, ct);
            if (cls is null)
            {
                return problem!;
            }

            var mine = await records.Where(r => r.ClassId == classId && (query.ClassCourseId == null || r.ClassCourseId == query.ClassCourseId)).ToListAsync(ct);
            var studentIds = mine.Select(r => r.StudentProfileId).Distinct().ToList();
            var students = await db.Set<StudentProfile>().Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
            rows = mine.GroupBy(r => r.StudentProfileId).Select(g =>
            {
                var s = students[g.Key];
                return new AttendanceReportRowDto(s.Id, s.NameAr, s.NameEn, s.StudentCode, null, null, null, AttendanceSummaries.Summarize(g.Select(x => x.Status)));
            }).OrderBy(r => r.StudentNumber).ToList();
        }

        var total = AttendanceSummaries.Summarize(rows.SelectMany(r => Enumerable.Repeat("Present", r.Summary.Present)
            .Concat(Enumerable.Repeat("Absent", r.Summary.Absent)).Concat(Enumerable.Repeat("Late", r.Summary.Late)).Concat(Enumerable.Repeat("Excused", r.Summary.Excused)))
            .Cast<string?>());
        var paged = rows.Skip((page - 1) * size).Take(size).ToList();
        return Results.Ok(new AttendanceReportDto(kind, start, to, total, new PagedResult<AttendanceReportRowDto>(paged, rows.Count, page, size), clock.UtcNow, clock.UtcNow));
    }

    private static async Task<IResult> MineAsync(
        [AsParameters] OwnAttendanceQuery query, LmsDbContext db, ICurrentContext current, IClientCalendar calendar, ISettingsResolver settings, IClock clock,
        ExceptionLookups lookups, CancellationToken ct)
    {
        if (current.UserId is not { } me)
        {
            return ApiProblems.NotFound();
        }

        var student = await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.UserId == me, ct);
        return student is null ? ApiProblems.Forbidden() : Results.Ok(await OwnAsync(student, isGuardian: false, query, db, calendar, settings, clock, lookups, ct));
    }

    private static async Task<IResult> ChildAsync(
        Guid id, [AsParameters] OwnAttendanceQuery query, LmsDbContext db, ICurrentContext current, IClientCalendar calendar, ISettingsResolver settings, IClock clock,
        ExceptionLookups lookups, CancellationToken ct)
    {
        if (current.UserId is not { } me)
        {
            return ApiProblems.NotFound();
        }

        // An active guardian link of the caller, never another family's child: a miss is 404.
        var guardianId = await db.Set<GuardianProfile>().Where(g => g.UserId == me).Select(g => (Guid?)g.Id).FirstOrDefaultAsync(ct);
        var linked = guardianId is { } gid && await db.Set<StudentGuardian>().AnyAsync(l => l.StudentId == id && l.GuardianId == gid, ct);
        var student = linked ? await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.Id == id, ct) : null;
        return student is null ? ApiProblems.NotFound() : Results.Ok(await OwnAsync(student, isGuardian: true, query, db, calendar, settings, clock, lookups, ct));
    }

    private static async Task<OwnAttendanceDto> OwnAsync(
        StudentProfile student, bool isGuardian, OwnAttendanceQuery query, LmsDbContext db, IClientCalendar calendar, ISettingsResolver settings, IClock clock,
        ExceptionLookups lookups, CancellationToken ct)
    {
        var today = await calendar.LocalTodayAsync(student.OrganizationId, ct);
        var to = query.To ?? today;
        var start = query.From ?? to.AddDays(-DefaultRangeDays);
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);
        var rows = await (from r in db.Set<AttendanceRecord>()
                          join s in db.Set<AttendanceSession>() on r.AttendanceSessionId equals s.Id
                          where r.StudentProfileId == student.Id && s.State == AttendanceSession.TakenState && s.Date >= start && s.Date <= to && r.Status != null
                          orderby s.Date descending, s.Id
                          select new { s.Id, s.Date, s.ClassCourseId, r.Status, r.Note }).ToListAsync(ct);

        // L21: the server answers whether the caller may submit an excuse: a guardian always, a student only under GuardiansAndStudents (D61).
        var submitters = (await settings.ResolveAsync(SettingKeys.ExcusesSubmitters, student.OrgUnitPath, ct)).Value;
        var canSubmit = isGuardian || submitters == "GuardiansAndStudents";
        var names = await CourseNamesAsync(db, lookups, student.OrganizationId, rows.Select(r => r.ClassCourseId).Distinct().ToList(), ct);
        var items = rows.Skip((page - 1) * size).Take(size).Select(r => new OwnAttendanceItemDto(
            r.Id, r.Date, names.GetValueOrDefault(r.ClassCourseId).Ar, names.GetValueOrDefault(r.ClassCourseId).En, null, null, r.Status!, r.Note,
            canSubmit && r.Status is AttendanceStatuses.Absent or AttendanceStatuses.Late, null, null)).ToList();
        return new OwnAttendanceDto(student.Id, student.NameAr, student.NameEn, start, to, AttendanceSummaries.Summarize(rows.Select(r => r.Status)),
            new PagedResult<OwnAttendanceItemDto>(items, rows.Count, page, size), canSubmit, clock.UtcNow);
    }

    private static async Task<Dictionary<Guid, (string Ar, string En)>> CourseNamesAsync(
        LmsDbContext db, ExceptionLookups lookups, Guid organizationId, IReadOnlyCollection<Guid> classCourseIds, CancellationToken ct)
    {
        var classCourses = await db.Set<ClassCourse>().Where(c => classCourseIds.Contains(c.Id)).ToListAsync(ct);
        var courses = await lookups.CoursesAsync(organizationId, classCourses.Select(c => c.CourseId).Distinct().ToList(), ct);
        return classCourses.ToDictionary(c => c.Id, c => courses.GetValueOrDefault(c.CourseId) is { } course ? (course.NameAr, course.NameEn) : (string.Empty, string.Empty));
    }
}
