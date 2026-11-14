using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Calendar;
using Lms.Domain.Excuses;
using Lms.Domain.Files;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Excuses;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Excuses;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Excuses;

/// <summary>
/// Submitting an excuse (FR-EXC-02, M5 Task 19, D61, L13, L21): the policy the submit dialog needs, the absences an excuse may still cover, the submission that freezes the
/// covered sessions, the caller's own submissions and the withdrawal. The caller reaches a student only as their linked guardian or as the student themself (anything
/// else is 404, never a leak); a student under <c>GuardiansOnly</c> is 403 <c>excuse_submitter_not_allowed</c>.
/// </summary>
internal static class ExcuseSubmitEndpoints
{
    private const int MaxSessions = 200;

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.StudentExcusePolicyPattern, PolicyAsync).RequirePermission(Permissions.Excuses.Submit);
        app.MapGet(Routes.StudentExcusableSessionsPattern, ExcusableAsync).RequirePermission(Permissions.Excuses.Submit);
        app.MapPost(Routes.ExcusesPattern, SubmitAsync).RequirePermission(Permissions.Excuses.Submit);
        app.MapGet(Routes.MeExcuses, MineAsync).RequirePermission(Permissions.Excuses.Submit);
        app.MapPost(Routes.ExcuseWithdrawPattern, WithdrawAsync).RequirePermission(Permissions.Excuses.Submit);
    }

    private static async Task<IResult> PolicyAsync(
        Guid id, ICurrentContext current, ExcuseSubmissionService service, IClock clock, ISettingsResolver settings, CancellationToken ct)
    {
        var relation = current.UserId is { } me ? await service.RelationAsync(id, me, ct) : null;
        if (relation is null)
        {
            return ApiProblems.NotFound();
        }

        var unit = relation.Student.OrgUnitPath;
        var rules = await service.RulesAsync(unit, ct);
        var canSubmit = await service.CanSubmitAsync(relation, ct);
        return Results.Ok(new ExcusePolicyDto(
            id, await service.SubmittersAsync(unit, ct), canSubmit, rules.MaxDaysAfterAbsence, rules.AllowedReasons.Order().ToList(),
            rules.AttachmentRequiredReasons.Order().ToList(), (await settings.ResolveAsync(SettingKeys.ExcusesMaxAttachmentMb, unit, ct)).Value, ExcuseLimits.AttachmentContentTypes, clock.UtcNow));
    }

    private static async Task<IResult> ExcusableAsync(
        Guid id, [AsParameters] ExcusableSessionsQuery query, ICurrentContext current, ExcuseSubmissionService service, CancellationToken ct)
    {
        var relation = current.UserId is { } me ? await service.RelationAsync(id, me, ct) : null;
        if (relation is null)
        {
            return ApiProblems.NotFound();
        }

        if (!await service.CanSubmitAsync(relation, ct))
        {
            return ApiProblems.Forbidden(ProblemCodes.excuse_submitter_not_allowed);
        }

        if (query.From > query.To || query.To.DayNumber - query.From.DayNumber > 92)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "range: invalid");
        }

        var absences = await service.AbsencesAsync(id, query.From, query.To, null, ct);
        var names = await service.CourseNamesAsync(relation.Student.OrganizationId, absences.Select(a => a.ClassCourseId).Distinct().ToList(), ct);
        return Results.Ok(absences.Select(a =>
        {
            var name = names.GetValueOrDefault(a.ClassCourseId);
            return new ExcusableSessionDto(a.SessionId, a.Date, name.Ar ?? string.Empty, name.En ?? string.Empty, null, null, a.Status);
        }).ToList());
    }

    private static async Task<IResult> SubmitAsync(
        SubmitExcuseRequest? body, LmsDbContext db, ICurrentContext current, ExcuseSubmissionService service, IClock clock, CancellationToken ct)
    {
        if (current.UserId is not { } me)
        {
            return ApiProblems.NotFound();
        }

        var bySessions = body?.SessionIds is not null;
        var byDates = body?.DateFrom is not null || body?.DateTo is not null;
        if (body is null || body.StudentId == Guid.Empty || string.IsNullOrWhiteSpace(body.Reason) || body.ReasonText?.Length > ExcuseLimits.ReasonTextMaxLength
            || bySessions == byDates || bySessions && body.SessionIds is not { Count: > 0 and <= MaxSessions } || byDates && (body.DateFrom is null || body.DateTo is null))
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "body: invalid"); // exactly one of session ids or a date range
        }

        var relation = await service.RelationAsync(body.StudentId, me, ct);
        if (relation is null)
        {
            return ApiProblems.NotFound();
        }

        if (!await service.CanSubmitAsync(relation, ct))
        {
            return ApiProblems.Forbidden(ProblemCodes.excuse_submitter_not_allowed);
        }

        if (body.AttachmentFileId is { } fileId && !await AttachmentUsableAsync(db, fileId, me, ct))
        {
            return ApiProblems.BadRequest(ProblemCodes.excuse_attachment_invalid);
        }

        var student = relation.Student;
        var today = await service.TodayAsync(student.OrganizationId, ct);
        var sessionIds = body.SessionIds?.Distinct().ToList();
        // By session ids the ids choose what is covered, so the whole history up to today is searched; by dates the range is the one the submitter chose.
        var candidates = await service.AbsencesAsync(body.StudentId, sessionIds is null ? body.DateFrom!.Value : DateOnly.MinValue, sessionIds is null ? body.DateTo!.Value : today, sessionIds, ct);
        var low = sessionIds is null ? body.DateFrom!.Value : candidates.Count > 0 ? candidates.Min(c => c.Date) : DateOnly.MinValue;
        var high = sessionIds is null ? body.DateTo!.Value : candidates.Count > 0 ? candidates.Max(c => c.Date) : today;
        var result = await ExcusePolicy.EvaluateAsync(
            await service.RulesAsync(student.OrgUnitPath, ct), body.Reason, body.AttachmentFileId is not null, low, high, today,
            candidates.Select(c => c.Date).ToList(), student.OrgUnitId, service.Calendar, ct);
        if (!result.IsAllowed)
        {
            return ApiProblems.Conflict(ProblemCodes.excuse_out_of_policy, extra: new { reason = result.Violation });
        }

        var covered = candidates.Where(c => result.CoverableDates.Contains(c.Date)).ToList();
        var excuse = new AbsenceExcuse(student, me, body.Reason, body.ReasonText?.Trim(), covered.Min(c => c.Date), covered.Max(c => c.Date), body.AttachmentFileId, clock.UtcNow);
        db.Add(excuse);
        db.AddRange(covered.Select(c => new AbsenceExcuseSession(excuse, c.SessionId)));
        await db.SaveChangesAsync(ct);
        return Results.Created($"{Routes.ExcusesPattern}/{excuse.Id}", (await ExcuseMapper.ToDtosAsync(db, [excuse], me, clock.UtcNow, ct))[0]);
    }

    /// <summary>The file must be one the caller uploaded and no other excuse holds (L10), so a file id learned elsewhere cannot be attached and read back.</summary>
    private static async Task<bool> AttachmentUsableAsync(LmsDbContext db, Guid fileId, Guid callerUserId, CancellationToken ct)
    {
        var file = await db.Set<StoredFile>().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        return file is not null && file.CreatedBy == callerUserId && !await db.Set<AbsenceExcuse>().AnyAsync(e => e.AttachmentFileId == fileId, ct);
    }

    private static async Task<IResult> MineAsync(
        [AsParameters] MyExcusesQuery query, LmsDbContext db, ICurrentContext current, ExcuseSubmissionService service, IClock clock, CancellationToken ct)
    {
        if (current.UserId is not { } me || query.Status is not null && !ExcuseStatuses.All.Contains(query.Status))
        {
            return current.UserId is null ? ApiProblems.NotFound() : ApiProblems.BadRequest(ProblemCodes.validation_failed, "status: invalid_value");
        }

        if (query.StudentId is { } studentId && await service.RelationAsync(studentId, me, ct) is null)
        {
            return ApiProblems.NotFound(); // a child that is not the caller's
        }

        var ownProfiles = db.Set<StudentProfile>().Where(s => s.UserId == me).Select(s => s.Id);
        var rows = db.Set<AbsenceExcuse>().Where(e => e.SubmittedByUserId == me || ownProfiles.Contains(e.StudentProfileId));
        if (query.StudentId is { } only)
        {
            rows = rows.Where(e => e.StudentProfileId == only);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(e => e.Status == status);
        }

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, Paging.MaxPageSize);
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(e => e.SubmittedAtUtc).ThenBy(e => e.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new PagedResult<ExcuseDto>(await ExcuseMapper.ToDtosAsync(db, items, me, clock.UtcNow, ct), total, page, size));
    }

    private static async Task<IResult> WithdrawAsync(Guid id, LmsDbContext db, ICurrentContext current, IClock clock, CancellationToken ct)
    {
        var excuse = await db.Set<AbsenceExcuse>().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (excuse is null || current.UserId is not { } me || excuse.SubmittedByUserId != me)
        {
            return ApiProblems.NotFound(); // only the submitter reaches it
        }

        try
        {
            excuse.Withdraw(me, clock.UtcNow);
        }
        catch (ExcuseTransitionException)
        {
            return ApiProblems.Conflict(ProblemCodes.excuse_not_pending);
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok((await ExcuseMapper.ToDtosAsync(db, [excuse], me, clock.UtcNow, ct))[0]);
    }
}
