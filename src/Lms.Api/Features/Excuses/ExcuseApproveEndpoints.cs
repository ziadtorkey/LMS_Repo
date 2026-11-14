using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Attendance;
using Lms.Domain.Excuses;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Lms.Shared.Excuses;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Excuses;

/// <summary>
/// Approving an excuse (FR-EXC-03, M5 Task 19, D61): the excuse moves to Approved and every covered record that is still Absent or Late becomes <c>Excused</c> with an
/// <c>AttendanceRecordChange</c> of kind <c>ExcuseApproved</c>, in <b>one</b> save (one transaction), so the excuse can never be Approved with its records untouched. A
/// record a teacher already changed (for example to Present) is left alone. The submitter is told after the save; a notification failure never undoes it.
/// </summary>
internal static class ExcuseApproveEndpoints
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost(Routes.ExcuseApprovePattern, ApproveAsync).RequireAuthorization().RequirePermission(Permissions.Excuses.Review);

    private static async Task<IResult> ApproveAsync(
        Guid id, ApproveExcuseRequest? request, HttpContext http, LmsDbContext db, IClock clock, ICurrentContext current, ExcuseNotifier notifier, CancellationToken ct)
    {
        var (excuse, problem) = await ExcuseReviewEndpoints.LoadAsync(id, Permissions.Excuses.Review, http, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        var comment = request?.Comment?.Trim();
        if (comment?.Length > ExcuseLimits.CommentMaxLength)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "comment: too_long");
        }

        var me = current.UserId!.Value;
        var now = clock.UtcNow;
        try
        {
            excuse!.Approve(me, string.IsNullOrEmpty(comment) ? null : comment, now);
        }
        catch (Exception ex) when (ex is ExcuseTransitionException or ExcuseReviewerIsSubmitterException)
        {
            return ExcuseReviewEndpoints.Refused(ex);
        }

        var sessionIds = await db.Set<AbsenceExcuseSession>().Where(l => l.AbsenceExcuseId == id).Select(l => l.AttendanceSessionId).ToListAsync(ct);
        var records = await db.Set<AttendanceRecord>()
            .Where(r => r.StudentProfileId == excuse.StudentProfileId && sessionIds.Contains(r.AttendanceSessionId)
                        && (r.Status == AttendanceStatuses.Absent || r.Status == AttendanceStatuses.Late)).ToListAsync(ct);
        var sessions = await db.Set<AttendanceSession>().Where(s => sessionIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        foreach (var record in records)
        {
            var from = record.Status;
            record.Set(AttendanceStatuses.Excused, record.Note);
            db.Add(new AttendanceRecordChange(record, from, AttendanceStatuses.Excused, AttendanceChangeKinds.ExcuseApproved, me, null, now));
        }

        foreach (var session in sessions.Values.Where(s => records.Any(r => r.AttendanceSessionId == s.Id)))
        {
            session.RecordSave(me, now); // already Taken, so only the version moves: the session version moves, so a teacher who loaded it earlier gets concurrency_conflict instead of overwriting the change
        }

        try
        {
            await db.SaveChangesAsync(ct); // the excuse and its records in one transaction
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.Conflict(ProblemCodes.concurrency_conflict);
        }

        await ExcuseReviewEndpoints.NotifyAsync(db, excuse, (s, e) => notifier.DecidedAsync(e, s, ct), ct);
        return Results.Ok((await ExcuseMapper.ToDtosAsync(db, [excuse], me, now, ct))[0]);
    }
}
