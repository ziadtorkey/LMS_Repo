using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Excuses;
using Lms.Domain.People;
using Lms.Infrastructure.Excuses;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Excuses;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Excuses;

/// <summary>
/// The review side of excuses (FR-EXC-03, M5 Task 19): the counselor queue, reject, escalate and the escalation targets. An excuse is read through the
/// unit-subtree filters, so one outside the caller's scope is a 404; the permission is then checked at the excuse's own unit. Approve waits for the attendance
/// tables (it must flip the covered records in the same transaction, Task 16/17) and has no endpoint yet.
/// </summary>
internal static class ExcuseReviewEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.ExcuseReviewPattern, QueueAsync).RequireAuthorization().RequirePermission(Permissions.Excuses.Review);
        app.MapPost(Routes.ExcuseRejectPattern, RejectAsync).RequireAuthorization().RequirePermission(Permissions.Excuses.Review);
        app.MapPost(Routes.ExcuseEscalatePattern, EscalateAsync).RequireAuthorization().RequirePermission(Permissions.Excuses.Escalate);
        app.MapGet(Routes.ExcuseEscalationTargetsPattern, TargetsAsync).RequireAuthorization();
    }

    private static async Task<IResult> QueueAsync(
        [AsParameters] ExcuseReviewQuery query, HttpContext http, LmsDbContext db, IClock clock, ICurrentContext current, CancellationToken ct)
    {
        if (query.Status is not null && !ExcuseStatuses.All.Contains(query.Status))
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "status: invalid_value");
        }

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, Paging.MaxPageSize);
        var rows = db.Set<AbsenceExcuse>().AsQueryable();
        if (query.UnitId is { } unitId)
        {
            var access = await http.CheckUnitAsync(Permissions.Excuses.Review, unitId, ct);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            rows = rows.Where(e => e.OrgUnitPath.StartsWith(access.Unit!.Path));
        }

        rows = query.Status is { } status
            ? rows.Where(e => e.Status == status)
            : rows.Where(e => e.Status == ExcuseStatuses.Submitted || e.Status == ExcuseStatuses.Escalated);
        if (query.StudentId is { } studentId)
        {
            rows = rows.Where(e => e.StudentProfileId == studentId);
        }

        var total = await rows.CountAsync(ct);
        var excuses = await rows.OrderBy(e => e.SubmittedAtUtc).ThenBy(e => e.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var now = clock.UtcNow;
        var dtos = await ExcuseMapper.ToDtosAsync(db, excuses, current.UserId ?? Guid.Empty, now, ct);
        var items = dtos.Select(d => new ExcuseReviewItemDto(d, [], Math.Max(0, (int)(now - d.SubmittedAtUtc).TotalDays))).ToList();
        return Results.Ok(new PagedResult<ExcuseReviewItemDto>(items, total, page, size));
    }

    private static async Task<IResult> RejectAsync(
        Guid id, RejectExcuseRequest? request, HttpContext http, LmsDbContext db, IClock clock, ICurrentContext current, ExcuseNotifier notifier, CancellationToken ct)
    {
        var (excuse, problem) = await LoadAsync(id, Permissions.Excuses.Review, http, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        var comment = request?.Comment?.Trim();
        if (string.IsNullOrEmpty(comment) || comment.Length > ExcuseLimits.CommentMaxLength)
        {
            return ApiProblems.BadRequest(ProblemCodes.excuse_comment_required);
        }

        try
        {
            excuse!.Reject(current.UserId!.Value, comment, clock.UtcNow);
        }
        catch (Exception ex) when (ex is ExcuseTransitionException or ExcuseReviewerIsSubmitterException)
        {
            return Refused(ex);
        }

        await db.SaveChangesAsync(ct);
        await NotifyAsync(db, excuse, (s, e) => notifier.DecidedAsync(e, s, ct), ct);
        return Results.Ok((await ExcuseMapper.ToDtosAsync(db, [excuse], current.UserId!.Value, clock.UtcNow, ct))[0]);
    }

    private static async Task<IResult> EscalateAsync(
        Guid id, EscalateExcuseRequest? request, HttpContext http, LmsDbContext db, IClock clock, ICurrentContext current, ExcuseEscalationTargets targets,
        ExcuseNotifier notifier, CancellationToken ct)
    {
        var (excuse, problem) = await LoadAsync(id, Permissions.Excuses.Escalate, http, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (request is null || request.TargetUserId == Guid.Empty || request.Comment?.Length > ExcuseLimits.CommentMaxLength)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "body: invalid");
        }

        var allowed = await targets.ForAsync(excuse!.OrganizationId, excuse.OrgUnitPath, ct);
        if (request.TargetUserId == excuse.SubmittedByUserId || request.TargetUserId == current.UserId
            || allowed.All(t => t.UserId != request.TargetUserId))
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "targetUserId: not_allowed");
        }

        try
        {
            excuse.Escalate(current.UserId!.Value, request.TargetUserId, request.Comment?.Trim(), clock.UtcNow);
        }
        catch (Exception ex) when (ex is ExcuseTransitionException or ExcuseReviewerIsSubmitterException)
        {
            return Refused(ex);
        }

        await db.SaveChangesAsync(ct);
        await NotifyAsync(db, excuse, (s, e) => notifier.EscalatedAsync(e, s, request.TargetUserId, ct), ct);
        return Results.Ok((await ExcuseMapper.ToDtosAsync(db, [excuse], current.UserId!.Value, clock.UtcNow, ct))[0]);
    }

    private static async Task<IResult> TargetsAsync(
        Guid id, HttpContext http, LmsDbContext db, ExcuseEscalationTargets targets, ICurrentContext current, CancellationToken ct)
    {
        // Excuses.Escalate or Excuses.Review at the excuse's unit; the submitter and the caller are never offered.
        var excuse = await db.Set<AbsenceExcuse>().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (excuse is null)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.Excuses.Escalate, excuse.OrgUnitId, ct);
        if (access.Problem is not null && (await http.CheckUnitAsync(Permissions.Excuses.Review, excuse.OrgUnitId, ct)).Problem is not null)
        {
            return access.Problem;
        }

        var candidates = await targets.ForAsync(excuse.OrganizationId, excuse.OrgUnitPath, ct);
        var list = candidates.Where(c => c.UserId != excuse.SubmittedByUserId && c.UserId != current.UserId)
            .OrderBy(c => c.RoleKey == BuiltInRoles.Manager ? 0 : 1).ThenBy(c => c.NameEn)
            .Select(c => new EscalationTargetDto(c.UserId, c.NameAr, c.NameEn, null, null, c.RoleKey == BuiltInRoles.Manager))
            .ToList();
        return Results.Ok(list);
    }

    private static async Task<(AbsenceExcuse? Excuse, IResult? Problem)> LoadAsync(
        Guid id, string permission, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var excuse = await db.Set<AbsenceExcuse>().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (excuse is null)
        {
            return (null, ApiProblems.NotFound());
        }

        var access = await http.CheckUnitAsync(permission, excuse.OrgUnitId, ct);
        return access.Problem is not null ? (null, access.Problem) : (excuse, null);
    }

    private static IResult Refused(Exception ex) => ex is ExcuseReviewerIsSubmitterException
        ? ApiProblems.Conflict(ProblemCodes.excuse_reviewer_is_submitter)
        : ApiProblems.Conflict(ProblemCodes.excuse_not_pending);

    private static async Task NotifyAsync(LmsDbContext db, AbsenceExcuse excuse, Func<StudentProfile, AbsenceExcuse, Task> send, CancellationToken ct)
    {
        var student = await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.Id == excuse.StudentProfileId, ct);
        if (student is not null)
        {
            await send(student, excuse);
        }
    }
}
