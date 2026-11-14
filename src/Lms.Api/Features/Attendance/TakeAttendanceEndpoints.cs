using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Attendance;
using Lms.Domain.Calendar;
using Lms.Domain.People;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Attendance;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Attendance;

/// <summary>
/// Taking attendance (FR-ATT-01, M5 Task 17): the records of a session, saving them with a default status plus exceptions in one call, the edit window and the
/// history. A relationship miss is 404; a teacher takes inside the window, only a user with Attendance.Edit after it (with a reason, L14).
/// </summary>
public static class TakeAttendanceEndpoints
{
    public static IEndpointRouteBuilder MapTakeAttendance(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.AttendanceSessionRecordsPattern, GetRecordsAsync).RequireAnyPermission(Permissions.Attendance.Take, Permissions.Attendance.Edit, Permissions.Attendance.View);
        app.MapPut(Routes.AttendanceSessionRecordsPattern, PutRecordsAsync).RequireAnyPermission(Permissions.Attendance.Take, Permissions.Attendance.Edit);
        app.MapGet(Routes.AttendanceSessionHistoryPattern, HistoryAsync).RequireAnyPermission(Permissions.Attendance.Take, Permissions.Attendance.Edit, Permissions.Attendance.View);
        return app;
    }

    private sealed record Access(AttendanceSession Session, bool CanTake, bool CanEdit, bool CanView);

    private static async Task<(Access? Access, IResult? Problem)> OpenAsync(
        Guid id, HttpContext http, LmsDbContext db, ICurrentContext current, IClientCalendar calendar, IAccessEvaluator evaluator, CancellationToken ct)
    {
        var session = await db.Set<AttendanceSession>().SingleOrDefaultAsync(s => s.Id == id, ct);
        if (session is null || current.UserId is not { } me)
        {
            return (null, ApiProblems.NotFound());
        }

        var today = await calendar.LocalTodayAsync(session.OrganizationId, ct);
        var teaches = (await http.CheckClassCourseAsync(Permissions.Attendance.Take, session.ClassCourseId, ct)).Problem is null;
        var substitutes = !teaches && session.SlotId is { } slotId
            && (await db.Set<TimetableException>().Where(e => e.SlotId == slotId && e.Date == session.Date).ToListAsync(ct))
                .Any(e => SubstituteRule.IsSubstituteOn(e.Kind, e.SubstituteUserId, me, e.Date, today));
        var canEdit = (await evaluator.EvaluateAsync(Permissions.Attendance.Edit, session.OrgUnitPath, ct)).Allowed;
        var canView = (await evaluator.EvaluateAsync(Permissions.Attendance.View, session.OrgUnitPath, ct)).Allowed;
        var access = new Access(session, teaches || substitutes, canEdit, canView);
        return access.CanTake || access.CanEdit || access.CanView ? (access, null) : (null, ApiProblems.NotFound());
    }

    private static async Task<IResult> GetRecordsAsync(
        Guid id, HttpContext http, LmsDbContext db, ICurrentContext current, IClientCalendar calendar, IAccessEvaluator evaluator, AttendanceRecordsMapper mapper, CancellationToken ct)
    {
        var (access, problem) = await OpenAsync(id, http, db, current, calendar, evaluator, ct);
        return access is null ? problem! : Results.Ok(await mapper.ToDtoAsync(access.Session, current.UserId!.Value, ct));
    }

    private static async Task<IResult> PutRecordsAsync(
        Guid id, TakeAttendanceRequest? body, HttpContext http, LmsDbContext db, ICurrentContext current, IClientCalendar calendar, IAccessEvaluator evaluator,
        AttendanceRoster roster, ISettingsResolver settings, IClock clock, AttendanceRecordsMapper mapper, AbsenceNotifier notifier, CancellationToken ct)
    {
        var (access, problem) = await OpenAsync(id, http, db, current, calendar, evaluator, ct);
        if (access is null)
        {
            return problem!;
        }

        if (body is null)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "body: required");
        }

        var session = access.Session;
        var now = clock.UtcNow;
        var hours = (await settings.ResolveAsync(SettingKeys.AttendanceEditWindowHours, session.OrgUnitPath, ct)).Value;
        var taken = session.State == AttendanceSession.TakenState;
        var inWindow = AttendanceTaker.InWindow(session.TakenAtUtc, hours, now);
        if (taken && inWindow ? !access.CanTake && !access.CanEdit : !taken && !access.CanTake)
        {
            return ApiProblems.Forbidden(); // a relationship hit but not the permission to take this session
        }

        if (session.Version != body.Version)
        {
            return ApiProblems.Conflict(ProblemCodes.concurrency_conflict, extra: new { current = await mapper.ToDtoAsync(session, current.UserId!.Value, ct) });
        }

        var today = await calendar.LocalTodayAsync(session.OrganizationId, ct);
        var students = await roster.StudentIdsAsync(session.ClassCourseId, session.Date, ct);
        var records = await db.Set<AttendanceRecord>().Where(r => r.AttendanceSessionId == session.Id).ToListAsync(ct);
        var existing = records.ToDictionary(r => r.StudentProfileId, r => (r.Status, r.Note));
        var marks = body.Records.Select(r => new TakeMark(r.StudentId, r.Status, r.Note)).ToList();
        var plan = AttendanceTaker.Plan(students, existing, body.DefaultStatus, marks, taken, inWindow, access.CanEdit, body.Reason, session.Date, today);
        if (!plan.Ok)
        {
            return plan.ProblemStatus switch
            {
                409 => ApiProblems.Conflict(plan.ProblemCode!),
                422 => ApiProblems.Unprocessable(plan.ProblemCode!),
                _ => ApiProblems.BadRequest(plan.ProblemCode!),
            };
        }

        var byStudent = records.ToDictionary(r => r.StudentProfileId);
        var kind = AttendanceTaker.ChangeKind(taken, inWindow);
        var changes = new List<AttendanceRecordChange>();
        var studentOf = new Dictionary<Guid, Guid>();
        foreach (var (student, (status, note)) in plan.Final)
        {
            var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, AttendanceLimits.NoteMaxLength)];
            if (!byStudent.TryGetValue(student, out var record))
            {
                record = new AttendanceRecord(session, student, status, cleanNote);
                db.Add(record);
                studentOf[record.Id] = student;
                changes.Add(new AttendanceRecordChange(record, null, status, kind, current.UserId!.Value, body.Reason?.Trim(), now));
                continue;
            }

            studentOf[record.Id] = student;
            if (record.Status != status)
            {
                changes.Add(new AttendanceRecordChange(record, record.Status, status, kind, current.UserId!.Value, body.Reason?.Trim(), now));
            }

            record.Set(status, cleanNote);
        }

        session.RecordSave(current.UserId!.Value, now);
        db.AddRange(changes);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.Conflict(ProblemCodes.concurrency_conflict);
        }

        // The family is told once per student whose new status is Absent or Late (first take or a change); never the note.
        await notifier.NotifyAsync(session, changes.Where(c => AbsenceNotifier.IsNoticeable(c.ToStatus)).Select(c => studentOf[c.AttendanceRecordId]).Distinct().ToList(), ct);
        return Results.Ok(await mapper.ToDtoAsync(session, current.UserId!.Value, ct));
    }

    private static async Task<IResult> HistoryAsync(
        Guid id, HttpContext http, LmsDbContext db, ICurrentContext current, IClientCalendar calendar, IAccessEvaluator evaluator, CancellationToken ct)
    {
        var (access, problem) = await OpenAsync(id, http, db, current, calendar, evaluator, ct);
        if (access is null)
        {
            return problem!;
        }

        var recordIds = db.Set<AttendanceRecord>().Where(r => r.AttendanceSessionId == id).Select(r => r.Id);
        var changes = await db.Set<AttendanceRecordChange>().Where(c => recordIds.Contains(c.AttendanceRecordId)).OrderBy(c => c.AtUtc).ThenBy(c => c.Id).ToListAsync(ct);
        var records = await db.Set<AttendanceRecord>().Where(r => r.AttendanceSessionId == id).ToDictionaryAsync(r => r.Id, ct);
        var studentIds = records.Values.Select(r => r.StudentProfileId).Distinct().ToList();
        var students = await db.Set<StudentProfile>().Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var userIds = changes.Select(c => c.ByUserId).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        return Results.Ok(changes.Select(c =>
        {
            var student = students.GetValueOrDefault(records[c.AttendanceRecordId].StudentProfileId);
            var user = users.GetValueOrDefault(c.ByUserId);
            return new AttendanceChangeDto(c.Id, records[c.AttendanceRecordId].StudentProfileId, student?.NameAr ?? string.Empty, student?.NameEn ?? string.Empty, c.Kind,
                c.FromStatus, c.ToStatus ?? string.Empty, c.Reason, c.ByUserId, user?.DisplayNameAr ?? string.Empty, user?.DisplayNameEn ?? string.Empty, c.AtUtc);
        }).ToList());
    }
}

/// <summary>Builds <see cref="AttendanceRecordsDto"/>: the session row, one row per roster student with the mark, and (Live, Task 18) the unmatched participants.</summary>
public sealed class AttendanceRecordsMapper(LmsDbContext db, AttendanceRoster roster, AttendanceListMapper listMapper, IClientCalendar calendar)
{
    public async Task<AttendanceRecordsDto> ToDtoAsync(AttendanceSession session, Guid callerUserId, CancellationToken ct)
    {
        var today = await calendar.LocalTodayAsync(session.OrganizationId, ct);
        var item = (await listMapper.ToDtosAsync([session], callerUserId, today, ct))[0];
        var studentIds = await roster.StudentIdsAsync(session.ClassCourseId, session.Date, ct);
        var students = await db.Set<StudentProfile>().Where(s => studentIds.Contains(s.Id)).OrderBy(s => s.StudentCode).ToListAsync(ct);
        var records = await db.Set<AttendanceRecord>().Where(r => r.AttendanceSessionId == session.Id).ToDictionaryAsync(r => r.StudentProfileId, ct);
        var rows = students.Select(s =>
        {
            records.TryGetValue(s.Id, out var r);
            return new AttendanceRecordDto(s.Id, s.NameAr, s.NameEn, s.StudentCode, r?.Status, r?.Note, null, null, false);
        }).ToList();
        return new AttendanceRecordsDto(item, rows, [], session.Source != AttendanceSession.LiveSource);
    }
}
