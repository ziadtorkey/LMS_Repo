using Lms.Api.Features.Content;
using Lms.Api.Features.Enrollment.Groups;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
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
/// Joining a live session and attaching its recording link (M4b Task 6, FR-VC-01). A student gets the join URL only inside
/// [start − <c>Live.JoinWindowMinutes</c>, end] by the server clock; staff with LiveSessions.Manage get the host start URL, other
/// staff the join URL. A student never receives the start URL, and a URL is never part of a list or a read.
/// </summary>
public sealed class LiveSessionJoinHandlers(
    LmsDbContext db, ModuleAccess access, LiveSessionService service, CompletionRecorder completion, ISettingsResolver settings,
    IClock clock, PlacementGuard guard)
{
    public const int RecordingUrlMaxLength = LiveSessionLimits.RecordingUrlMaxLength;

    public async Task<IResult> JoinAsync(Guid id, HttpContext http, CancellationToken ct)
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

        var module = await db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Kind == ModuleKinds.LiveSession && m.RefId == session.Id, ct);
        if (module is null)
        {
            return ApiProblems.NotFound();
        }

        var now = clock.UtcNow;
        var isStudent = await access.IsStudentAsync(ct);
        Guid? studentId = null;
        if (isStudent)
        {
            studentId = await access.StudentIdAsync(ct);
            if (studentId is null || !await StudentSeesAsync(module, studentId.Value, ct))
            {
                return ApiProblems.NotFound();
            }
        }

        if (!(await settings.ResolveAsync(FeatureFlags.VirtualClasses, cc!.OrgUnitPath, ct)).Value)
        {
            return FeatureDisabled();
        }

        var canManage = !isStudent && (await http.CheckClassCourseAsync(Permissions.LiveSessions.Manage, cc.Id, ct)).Problem is null;
        var window = (await settings.ResolveAsync(SettingKeys.LiveJoinWindowMinutes, cc.OrgUnitPath, ct)).Value;
        var open = session.Status is LiveSessionRules.Scheduled or LiveSessionRules.Live
                   && (!isStudent || (now >= session.StartsAtUtc.AddMinutes(-window) && now <= session.EndsAtUtc));
        var url = open ? service.UrlOf(session, canManage) : null;
        if (url is null)
        {
            return ApiProblems.Conflict(ProblemCodes.live_session_not_open, "The session is not open to join.");
        }

        if (studentId is { } student)
        {
            await completion.RecordAsync(module, student, CompletionSignal.Viewed, null, ct);
        }

        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new LiveSessionJoinDto(url, LiveSessionMapper.Utc(session.EndsAtUtc), now));
    }

    public async Task<IResult> SetRecordingAsync(Guid id, RecordingLinkRequest? request, HttpContext http, CancellationToken ct)
    {
        var session = await db.Set<LiveSession>().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null)
        {
            return ApiProblems.NotFound();
        }

        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.LiveSessions.Manage, session.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (!ValidRecordingUrl(request?.Url, out var url))
        {
            return Lms.Api.Features.Classes.ClassValidators.Invalid(nameof(RecordingLinkRequest.Url), "http_or_https_url");
        }

        if (!(await settings.ResolveAsync(FeatureFlags.VirtualClasses, cc!.OrgUnitPath, ct)).Value)
        {
            return FeatureDisabled();
        }

        if (await GroupEndpoints.WritableAsync(cc, db, guard, ct) is { } closed)
        {
            return closed;
        }

        session.SetRecording(url, LiveSessionRules.RecordingManual);
        await db.SaveChangesAsync(ct);
        var module = await db.Set<LessonModule>().FirstAsync(m => m.Kind == ModuleKinds.LiveSession && m.RefId == session.Id, ct);
        var window = (await settings.ResolveAsync(SettingKeys.LiveJoinWindowMinutes, cc.OrgUnitPath, ct)).Value;
        return Results.Ok(await new LiveSessionMapper(db).SessionAsync(session, module, true, window, clock.UtcNow, ct));
    }

    /// <summary>http or https only, absolute, at most 2048 characters, no whitespace or control characters (never <c>javascript:</c> or <c>data:</c>).</summary>
    public static bool ValidRecordingUrl(string? value, out string url)
    {
        url = value?.Trim() ?? string.Empty;
        return url.Length is > 0 and <= RecordingUrlMaxLength
               && !url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
               && Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && uri.Host.Length > 0;
    }

    private async Task<bool> StudentSeesAsync(LessonModule module, Guid studentId, CancellationToken ct)
    {
        var group = await access.GroupOfAsync(module.ClassCourseId, studentId, ct);
        return await access.VisibleTo(module.ClassCourseId, group, clock.UtcNow).AnyAsync(p => p.Module.Id == module.Id, ct);
    }

    internal static IResult FeatureDisabled() => Results.Problem(
        statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: "Virtual classes are not enabled.",
        extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.feature_disabled });
}
