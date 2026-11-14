using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Attendance;
using Lms.Domain.Calendar;
using Lms.Infrastructure.Attendance;
using Lms.Infrastructure.Attendance.Live;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Attendance;

/// <summary>
/// The live-session attendance proposal (FR-TT-06, M5 Task 18, L11): the teacher maps the guests the server could not match, then confirms the proposal as it stands.
/// Both calls are for a <c>Live</c> proposal only (409 <c>attendance_not_a_proposal</c> otherwise or once confirmed); another teacher gets 404. Guardians are told only on
/// confirmation, never when the proposal is created (item 10).
/// </summary>
public static class LiveProposalEndpoints
{
    public static IEndpointRouteBuilder MapLiveProposal(this IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.AttendanceSessionConfirmPattern, ConfirmAsync).RequirePermission(Permissions.Attendance.Take);
        app.MapPut(Routes.AttendanceSessionLiveMappingsPattern, MapAsync).RequirePermission(Permissions.Attendance.Take);
        return app;
    }

    private static async Task<IResult> ConfirmAsync(
        Guid id, ConfirmAttendanceRequest? body, HttpContext http, LmsDbContext db, ICurrentContext current, IClientCalendar calendar, IAccessEvaluator evaluator,
        AttendanceRoster roster, IClock clock, AttendanceRecordsMapper mapper, AbsenceNotifier notifier, CancellationToken ct)
    {
        var (access, problem) = await TakeAttendanceEndpoints.OpenAsync(id, http, db, current, calendar, evaluator, ct);
        if (access is null)
        {
            return problem!;
        }

        if (body is null)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "body: required");
        }

        var session = access.Session;
        if (!access.CanTake)
        {
            return ApiProblems.Forbidden(); // a view or edit hit is not the right to take this session
        }

        if (!session.IsProposal)
        {
            return ApiProblems.Conflict(ProblemCodes.attendance_not_a_proposal);
        }

        if (session.Version != body.Version)
        {
            return ApiProblems.Conflict(ProblemCodes.concurrency_conflict, extra: new { current = await mapper.ToDtoAsync(session, current.UserId!.Value, ct) });
        }

        var students = await roster.StudentIdsAsync(session.ClassCourseId, session.Date, ct);
        var records = await db.Set<AttendanceRecord>().Where(r => r.AttendanceSessionId == session.Id).ToListAsync(ct);
        var marked = records.Where(r => r.Status is not null).Select(r => r.StudentProfileId).ToHashSet();
        if (students.Any(s => !marked.Contains(s)))
        {
            return ApiProblems.Unprocessable(ProblemCodes.attendance_unmarked_rows); // a student enrolled after the proposal: mark them first
        }

        var now = clock.UtcNow;
        var me = current.UserId!.Value;
        var changes = records.Select(r => new AttendanceRecordChange(r, null, r.Status, "Taken", me, null, now)).ToList();
        session.RecordSave(me, now);
        db.AddRange(changes);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.Conflict(ProblemCodes.concurrency_conflict);
        }

        await notifier.NotifyAsync(session, records.Where(r => AbsenceNotifier.IsNoticeable(r.Status!)).Select(r => r.StudentProfileId).Distinct().ToList(), ct);
        return Results.Ok(await mapper.ToDtoAsync(session, me, ct));
    }

    private static async Task<IResult> MapAsync(
        Guid id, LiveMappingRequest? body, HttpContext http, LmsDbContext db, ICurrentContext current, IClientCalendar calendar, IAccessEvaluator evaluator,
        LiveProposalService proposals, AttendanceRecordsMapper mapper, CancellationToken ct)
    {
        var (access, problem) = await TakeAttendanceEndpoints.OpenAsync(id, http, db, current, calendar, evaluator, ct);
        if (access is null)
        {
            return problem!;
        }

        if (body is null)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "body: required");
        }

        var session = access.Session;
        if (!access.CanTake)
        {
            return ApiProblems.NotFound(); // mapping is for the teacher of the course only
        }

        if (!session.IsProposal)
        {
            return ApiProblems.Conflict(ProblemCodes.attendance_not_a_proposal);
        }

        if (session.Version != body.Version)
        {
            return ApiProblems.Conflict(ProblemCodes.concurrency_conflict, extra: new { current = await mapper.ToDtoAsync(session, current.UserId!.Value, ct) });
        }

        var outcome = await proposals.MapAsync(session, body.Mappings, ct);
        if (!outcome.Ok)
        {
            return ApiProblems.BadRequest(outcome.ProblemCode!);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex is DbUpdateException && AttendanceSessionGenerator.IsDuplicate((DbUpdateException)ex))
        {
            return ApiProblems.Conflict(ProblemCodes.concurrency_conflict); // another save moved the session, or the same participant was mapped twice at once
        }

        return Results.Ok(await mapper.ToDtoAsync(session, current.UserId!.Value, ct));
    }
}
