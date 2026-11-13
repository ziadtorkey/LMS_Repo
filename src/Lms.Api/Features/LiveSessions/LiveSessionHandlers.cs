using Lms.Api.Features.Classes;
using Lms.Api.Features.Content;
using Lms.Api.Features.Enrollment.Groups;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.LiveSessions;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.LiveSessions;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.LiveSessions;

/// <summary>
/// Live session authoring and reading (M4b Task 4, FR-VC-01). Writes need LiveSessions.Manage and Modules.Manage with the
/// relationship to the class course, the <c>VirtualClasses</c> flag and the closed class guard. A student reads a session only
/// while its module is visible to them (404 otherwise). No response carries a join or start URL.
/// </summary>
public sealed class LiveSessionHandlers(
    LmsDbContext db, ModuleAccess access, LiveSessionService service, ISettingsResolver settings, IClock clock, PlacementGuard guard)
{
    private LiveSessionMapper Mapper => new(db);

    public async Task<IResult> ListAsync(Guid classCourseId, LiveSessionQuery query, HttpContext http, CancellationToken ct)
    {
        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.LiveSessions.View, classCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var now = clock.UtcNow;
        IQueryable<LessonModule> modules;
        if (await access.IsStudentAsync(ct))
        {
            if (await access.StudentIdAsync(ct) is not { } studentId)
            {
                return Results.Ok(new PagedResult<LiveSessionListItemDto>([], 0, 1, Paging.DefaultPageSize));
            }

            var group = await access.GroupOfAsync(cc!.Id, studentId, ct);
            modules = access.VisibleTo(cc.Id, group, now).Select(p => p.Module);
        }
        else
        {
            modules = db.Set<LessonModule>().Where(m => m.ClassCourseId == cc!.Id);
        }

        var rows = from s in db.Set<LiveSession>()
                   join m in modules on s.Id equals m.RefId
                   where s.ClassCourseId == cc!.Id && m.Kind == ModuleKinds.LiveSession
                   select new { S = s, M = m };
        if (query.FromUtc is { } from)
        {
            rows = rows.Where(r => r.S.StartsAtUtc >= from);
        }

        if (query.ToUtc is { } to)
        {
            rows = rows.Where(r => r.S.StartsAtUtc <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            rows = rows.Where(r => r.S.Status == query.Status);
        }

        var total = await rows.CountAsync(ct);
        var size = Math.Clamp(query.PageSize, 1, Paging.MaxPageSize);
        var number = Math.Max(query.Page, 1);
        var page = await rows.OrderBy(r => r.S.StartsAtUtc).ThenBy(r => r.S.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
        var items = page.Select(r => new LiveSessionListItemDto(
            r.S.Id, r.M.Id, r.S.SeriesId, r.S.OccurrenceIndex, r.M.TitleAr, r.M.TitleEn, LiveSessionMapper.Utc(r.S.StartsAtUtc),
            LiveSessionMapper.Utc(r.S.EndsAtUtc), r.S.Status, r.S.RecordingUrl is not null, now)).ToList();
        return Results.Ok(new PagedResult<LiveSessionListItemDto>(items, total, number, size));
    }

    public async Task<IResult> CreateAsync(Guid classCourseId, CreateLiveSessionRequest? request, HttpContext http, CancellationToken ct)
    {
        var (cc, problem) = await ManageAsync(http, classCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var body = request is null ? null : LiveSessionValidators.Of(request);
        if (LiveSessionValidators.Validate(body) is { } invalid)
        {
            return invalid;
        }

        if (await FlagOffAsync(cc!, ct) is { } disabled)
        {
            return disabled;
        }

        if (await GroupEndpoints.WritableAsync(cc!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        try
        {
            var sessions = await service.CreateAsync(
                cc!, request!.LessonKey, LiveSessionValidators.MetadataOf(body!.Module!), body.StartsAtUtc, body.DurationMinutes,
                LiveSessionValidators.RuleOf(body.Recurrence), ct);
            var window = await JoinWindowAsync(cc!, ct);
            var dtos = new List<LiveSessionDto>();
            foreach (var session in sessions)
            {
                var module = await db.Set<LessonModule>().FirstAsync(m => m.Id == session.ModuleId, ct);
                dtos.Add(await Mapper.SessionAsync(session, module, true, window, clock.UtcNow, ct));
            }

            return Results.Created(Routes.LiveSession(sessions[0].Id), dtos);
        }
        catch (Exception ex) when (ex is RecurrenceInvalidException or ModuleRuleException or LiveSessionCapException)
        {
            return Failure(ex, http);
        }
    }

    public async Task<IResult> GetAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var session = await db.Set<LiveSession>().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null)
        {
            return ApiProblems.NotFound();
        }

        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.LiveSessions.View, session.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var module = await ModuleOfAsync(session.Id, ct);
        if (module is null)
        {
            return ApiProblems.NotFound();
        }

        var isStudent = await access.IsStudentAsync(ct);
        if (isStudent && !await StudentSeesAsync(module, ct))
        {
            return ApiProblems.NotFound();
        }

        var canEdit = !isStudent && (await http.CheckClassCourseAsync(Permissions.LiveSessions.Manage, cc!.Id, ct)).Problem is null;
        return Results.Ok(await Mapper.SessionAsync(session, module, canEdit, await JoinWindowAsync(cc!, ct), clock.UtcNow, ct));
    }

    public async Task<IResult> UpdateAsync(Guid id, UpdateLiveSessionRequest? request, HttpContext http, CancellationToken ct)
    {
        var session = await db.Set<LiveSession>().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null)
        {
            return ApiProblems.NotFound();
        }

        var (cc, problem) = await ManageAsync(http, session.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var body = request is null ? null : LiveSessionValidators.Of(request);
        if (LiveSessionValidators.Validate(body) is { } invalid)
        {
            return invalid;
        }

        if (body!.Scope == SeriesEditScopes.ThisAndFollowing && session.SeriesId is null)
        {
            return ClassValidators.Invalid(nameof(UpdateLiveSessionRequest.Scope), "single_session_scope_this");
        }

        if (body.Recurrence is not null && (body.Scope != SeriesEditScopes.ThisAndFollowing || session.SeriesId is null))
        {
            return ApiProblems.BadRequest(ProblemCodes.recurrence_invalid, "A recurrence is only accepted with ThisAndFollowing on a series.");
        }

        if (await FlagOffAsync(cc!, ct) is { } disabled)
        {
            return disabled;
        }

        if (await GroupEndpoints.WritableAsync(cc!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        if (session.Status is LiveSessionRules.Live or LiveSessionRules.Ended or LiveSessionRules.Cancelled)
        {
            return ApiProblems.Conflict(ProblemCodes.live_session_not_open, "Only a scheduled session can be changed.");
        }

        try
        {
            await service.UpdateAsync(
                session, cc!, LiveSessionValidators.MetadataOf(body.Module!), body.StartsAtUtc, body.DurationMinutes, body.Scope,
                LiveSessionValidators.RuleOf(body.Recurrence), ct);
            var module = await ModuleOfAsync(session.Id, ct);
            return Results.Ok(await Mapper.SessionAsync(session, module!, true, await JoinWindowAsync(cc!, ct), clock.UtcNow, ct));
        }
        catch (Exception ex) when (ex is RecurrenceInvalidException or ModuleRuleException or LiveSessionCapException)
        {
            return Failure(ex, http);
        }
    }

    public async Task<IResult> DeleteAsync(Guid id, string? scope, HttpContext http, CancellationToken ct)
    {
        var session = await db.Set<LiveSession>().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null)
        {
            return ApiProblems.NotFound();
        }

        var (cc, problem) = await ManageAsync(http, session.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        scope ??= SeriesEditScopes.This;
        if (!SeriesEditScopes.All.Contains(scope))
        {
            return ClassValidators.Invalid("scope", "invalid_value");
        }

        if (scope == SeriesEditScopes.ThisAndFollowing && session.SeriesId is null)
        {
            return ClassValidators.Invalid("scope", "single_session_scope_this");
        }

        if (await FlagOffAsync(cc!, ct) is { } disabled)
        {
            return disabled;
        }

        if (await GroupEndpoints.WritableAsync(cc!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        await service.DeleteAsync(session, scope, ct);
        return Results.NoContent();
    }

    /// <summary>LiveSessions.Manage and Modules.Manage at the class course (both, with the relationship filter).</summary>
    private static async Task<(ClassCourse? Cc, IResult? Problem)> ManageAsync(HttpContext http, Guid classCourseId, CancellationToken ct)
    {
        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.LiveSessions.Manage, classCourseId, ct);
        if (problem is not null)
        {
            return (null, problem);
        }

        return await http.CheckClassCourseAsync(Permissions.Modules.Manage, classCourseId, ct);
    }

    /// <summary>403 <c>feature_disabled</c> when <c>Features.VirtualClasses</c> is off at the unit of the class course.</summary>
    private async Task<IResult?> FlagOffAsync(ClassCourse cc, CancellationToken ct) =>
        (await settings.ResolveAsync(FeatureFlags.VirtualClasses, cc.OrgUnitPath, ct)).Value
            ? null
            : Results.Problem(
                statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: "Virtual classes are not enabled.",
                extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.feature_disabled });

    private async Task<int> JoinWindowAsync(ClassCourse cc, CancellationToken ct) =>
        (await settings.ResolveAsync(SettingKeys.LiveJoinWindowMinutes, cc.OrgUnitPath, ct)).Value;

    private Task<LessonModule?> ModuleOfAsync(Guid sessionId, CancellationToken ct) =>
        db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Kind == ModuleKinds.LiveSession && m.RefId == sessionId, ct);

    /// <summary>The module is visible now to the calling student (published or in its window, for their class course or group).</summary>
    private async Task<bool> StudentSeesAsync(LessonModule module, CancellationToken ct)
    {
        if (await access.StudentIdAsync(ct) is not { } studentId)
        {
            return false;
        }

        var group = await access.GroupOfAsync(module.ClassCourseId, studentId, ct);
        return await access.VisibleTo(module.ClassCourseId, group, clock.UtcNow).AnyAsync(p => p.Module.Id == module.Id, ct);
    }

    /// <summary>Rule failures of the service as the problems the contract names; anything else is a real error.</summary>
    private IResult Failure(Exception ex, HttpContext http)
    {
        db.ChangeTracker.Clear();
        switch (ex)
        {
            case RecurrenceInvalidException recurrence:
                return ApiProblems.BadRequest(ProblemCodes.recurrence_invalid, recurrence.Message);
            case ModuleRuleException rule:
                return ApiProblems.BadRequest(rule.Code, rule.Message);
            case LiveSessionCapException cap:
                http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling((cap.ResetsAtUtc - clock.UtcNow).TotalSeconds)).ToString();
                return ApiProblems.Conflict(ProblemCodes.provider_unavailable, "The daily live session limit of the client is reached.");
            default:
                throw new InvalidOperationException("Not a live session rule failure.", ex);
        }
    }
}
