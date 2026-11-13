using Lms.Api.Features.Content;
using Lms.Api.Features.Enrollment.Groups;
using Lms.Api.Infrastructure;
using Lms.Api.PackageHost;
using Lms.Domain.Abstractions;
using Lms.Domain.Content;
using Lms.Domain.Packages;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Packages;
using Lms.Infrastructure.Packages.Attempts;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Content;
using Lms.Shared.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lms.Api.Features.Packages;

/// <summary>
/// Launching, previewing and tracking a package (M4b Task 9b, FR-STD-01/02, X-05, D57). A student launches (the module must be visible to
/// them; staff get 404 and use preview), the page then commits what the content reports over its private port and finishes. Every
/// check the page only guards is enforced here: the attempt's own student, an open attempt, a visible module, sizes, keys, rate.
/// </summary>
public sealed class PackageAttemptHandlers(
    LmsDbContext db, ModuleAccess access, PackageAttemptService service, PackageTokenService tokens, ISettingsResolver settings,
    IOptions<PackageHostOptions> host, IClock clock, CompletionRecorder completion, PlacementGuard guard)
{
    private const int MinTokenMinutes = 5;
    private const int MaxTokenMinutes = 480;

    public async Task<IResult> LaunchAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var ctx = await StudentContextAsync(id, http, ct);
        if (ctx.Problem is not null)
        {
            return ctx.Problem;
        }

        var (upload, module, studentId) = (ctx.Upload!, ctx.Module!, ctx.StudentId);
        if (await PackageSupport.FlagAsync(ctx.Course!, settings, ct) is { } disabled)
        {
            return disabled;
        }

        if (PackageSupport.HostProblem(http, host) is { } noHost)
        {
            return noHost;
        }

        if (upload.Status != PackageUploadStatuses.Ready)
        {
            return ApiProblems.Conflict(ProblemCodes.package_not_ready);
        }

        var (attempt, resumed) = await service.OpenAsync(upload, module, studentId, ct);
        await completion.RecordAsync(module, studentId, CompletionSignal.Viewed, null, ct);
        var now = clock.UtcNow;
        var (url, expires) = await UrlAsync(upload, studentId, attempt.Id, false, now, ct);
        return Results.Ok(new PackageLaunchDto(url, attempt.Id, expires, now, resumed));
    }

    public async Task<IResult> PreviewAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var upload = await db.Set<PackageUpload>().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (upload is null)
        {
            return ApiProblems.NotFound();
        }

        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Packages.View, upload.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await access.IsStudentAsync(ct))
        {
            return Forbidden();
        }

        if (PackageSupport.HostProblem(http, host) is { } noHost)
        {
            return noHost;
        }

        if (upload.Status != PackageUploadStatuses.Ready)
        {
            return ApiProblems.Conflict(ProblemCodes.package_not_ready);
        }

        _ = cc;
        var now = clock.UtcNow;
        var (url, expires) = await UrlAsync(upload, null, null, true, now, ct);
        return Results.Ok(new PackagePreviewDto(url, expires, now));
    }

    public async Task<IResult> MyStateAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var ctx = await StudentContextAsync(id, http, ct);
        if (ctx.Problem is not null)
        {
            return ctx.Problem;
        }

        var upload = ctx.Upload!;
        var attempts = db.Set<PackageAttempt>().Where(a => a.PackageId == upload.Id && a.StudentId == ctx.StudentId);
        var latest = await attempts.OrderByDescending(a => a.StartedAtUtc).ThenByDescending(a => a.Id).FirstOrDefaultAsync(ct);
        var graded = await service.GradedAsync(upload, ctx.StudentId, ct);
        return Results.Ok(new PackageStudentStateDto(
            upload.GradingMethod ?? PackageGradingMethods.Highest, latest is null ? null : ToDto(latest),
            await attempts.AnyAsync(a => a.Status == PackageAttempt.CompletedStatus, ct), graded?.ScoreRaw));
    }

    public async Task<IResult> CommitAsync(Guid attemptId, PackageCommitRequest? request, HttpContext http, CancellationToken ct)
    {
        var ctx = await AttemptContextAsync(attemptId, ct);
        if (ctx.Problem is not null)
        {
            return ctx.Problem;
        }

        if (request is null || CommitGate.Check(ctx.Upload!, request) != CommitRefusal.None)
        {
            return ApiProblems.BadRequest(ProblemCodes.package_commit_rejected, "The commit is not valid for this package.");
        }

        var interval = (await settings.ResolveAsync(SettingKeys.PackagesCommitMinIntervalSeconds, ctx.Attempt!.OrgUnitPath, ct)).Value;
        var outcome = await service.CommitAsync(attemptId, ctx.Upload!, ctx.Module!, request, interval, ct);
        return outcome switch
        {
            AttemptOutcome.Applied or AttemptOutcome.Ignored => Results.NoContent(),
            AttemptOutcome.RateLimited => Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests, title: "Too many requests",
                extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.package_commit_rejected }),
            AttemptOutcome.TooLarge => ApiProblems.BadRequest(ProblemCodes.package_commit_rejected, "The data model is too large."),
            _ => ApiProblems.Conflict(ProblemCodes.package_commit_rejected),
        };
    }

    public async Task<IResult> FinishAsync(Guid attemptId, PackageFinishRequest? request, HttpContext http, CancellationToken ct)
    {
        var ctx = await AttemptContextAsync(attemptId, ct);
        if (ctx.Problem is not null)
        {
            return ctx.Problem;
        }

        if (request is null || request.LastSeq < -1)
        {
            return ApiProblems.BadRequest(ProblemCodes.package_commit_rejected, "The finish is not valid.");
        }

        var (outcome, attempt) = await service.FinishAsync(attemptId, ctx.Upload!, ctx.Module!, request.LastSeq, ct);
        if (outcome != AttemptOutcome.Applied || attempt is null)
        {
            return ApiProblems.Conflict(ProblemCodes.package_commit_rejected);
        }

        if (attempt.Status == PackageAttempt.CompletedStatus)
        {
            // Completion and the grade follow from what the server judged, never from what the page claims.
            var module = ctx.Module!;
            await completion.RecordAsync(
                module, attempt.StudentId, module.CompletionRule == CompletionRules.Score ? CompletionSignal.Scored : CompletionSignal.Submitted,
                attempt.ScoreRaw, ct);
            await service.GradeAsync(ctx.Upload!, attempt.StudentId, ct);
        }

        return Results.Ok(ToDto(attempt));
    }

    public async Task<IResult> SetGradingMethodAsync(Guid id, UpdatePackageGradingMethodRequest? request, HttpContext http, CancellationToken ct)
    {
        var upload = await db.Set<PackageUpload>().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (upload is null)
        {
            return ApiProblems.NotFound();
        }

        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Modules.Manage, upload.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (request is null || !PackageGradingMethods.All.Contains(request.GradingMethod))
        {
            return PackageSupport.Invalid("gradingMethod", "invalid_value");
        }

        if (upload.ModuleId is null)
        {
            return ApiProblems.Conflict(ProblemCodes.package_not_attached);
        }

        if (await GroupEndpoints.WritableAsync(cc!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        upload.SetGradingMethod(request.GradingMethod);
        await db.SaveChangesAsync(ct);
        await service.RegradeAllAsync(upload, ct);
        return Results.Ok(PackageSupport.ToDto(upload));
    }

    private sealed record StudentContext(
        PackageUpload? Upload, LessonModule? Module, Domain.Classes.ClassCourse? Course, Guid StudentId, IResult? Problem);

    /// <summary>
    /// The package for a student caller: the package and its ClassCourse are reached by relationship (a miss is 404), staff are 404 (they
    /// use preview), and the module must be attached and visible to the student's group now.
    /// </summary>
    private async Task<StudentContext> StudentContextAsync(Guid packageId, HttpContext http, CancellationToken ct)
    {
        var upload = await db.Set<PackageUpload>().FirstOrDefaultAsync(p => p.Id == packageId, ct);
        if (upload is null)
        {
            return Fail(ApiProblems.NotFound());
        }

        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Packages.View, upload.ClassCourseId, ct);
        if (problem is not null)
        {
            return Fail(problem);
        }

        if (!await access.IsStudentAsync(ct) || await access.StudentIdAsync(ct) is not { } studentId || upload.ModuleId is null)
        {
            return Fail(ApiProblems.NotFound());
        }

        var module = await db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Id == upload.ModuleId, ct);
        if (module is null || !await StudentSeesAsync(module, studentId, ct))
        {
            return Fail(ApiProblems.NotFound());
        }

        return new StudentContext(upload, module, cc, studentId, null);
    }

    private sealed record AttemptContext(PackageAttempt? Attempt, PackageUpload? Upload, LessonModule? Module, IResult? Problem);

    /// <summary>The attempt for commit and finish: only its own student, and only while the module is still visible to them (a miss is 404).</summary>
    private async Task<AttemptContext> AttemptContextAsync(Guid attemptId, CancellationToken ct)
    {
        var attempt = await db.Set<PackageAttempt>().FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null || !await access.IsStudentAsync(ct) || await access.StudentIdAsync(ct) != attempt.StudentId)
        {
            return new AttemptContext(null, null, null, ApiProblems.NotFound());
        }

        var upload = await db.Set<PackageUpload>().FirstOrDefaultAsync(p => p.Id == attempt.PackageId, ct);
        var module = await db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Id == attempt.ModuleId, ct);
        if (upload is null || module is null || !await StudentSeesAsync(module, attempt.StudentId, ct))
        {
            return new AttemptContext(null, null, null, ApiProblems.NotFound());
        }

        return new AttemptContext(attempt, upload, module, null);
    }

    private async Task<(string Url, DateTime ExpiresAtUtc)> UrlAsync(
        PackageUpload upload, Guid? student, Guid? attempt, bool preview, DateTime now, CancellationToken ct)
    {
        var minutes = Math.Clamp((await settings.ResolveAsync(SettingKeys.PackagesTokenMinutes, upload.OrgUnitPath, ct)).Value, MinTokenMinutes, MaxTokenMinutes);
        var lifetime = TimeSpan.FromMinutes(minutes);
        var token = tokens.Issue(
            upload.OrganizationId, upload.OrgUnitPath, upload.Id, student, attempt, preview, lifetime, new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)));
        var entry = service.TrackerFor(upload.Kind).LaunchPath(upload);
        return ($"{tokens.Audience}/p/{token}/{entry}", DateTime.SpecifyKind(now + lifetime, DateTimeKind.Utc));
    }

    private async Task<bool> StudentSeesAsync(LessonModule module, Guid studentId, CancellationToken ct)
    {
        var group = await access.GroupOfAsync(module.ClassCourseId, studentId, ct);
        return await access.VisibleTo(module.ClassCourseId, group, clock.UtcNow).AnyAsync(p => p.Module.Id == module.Id, ct);
    }

    private static PackageAttemptDto ToDto(PackageAttempt a) => new(
        a.Id, a.Status, a.Status == PackageAttempt.CompletedStatus, a.ScoreRaw, a.ScoreMin, a.ScoreMax, a.LastSeq,
        a.CompletedAtUtc is { } done ? DateTime.SpecifyKind(done, DateTimeKind.Utc) : null);

    private static StudentContext Fail(IResult problem) => new(null, null, null, Guid.Empty, problem);

    private static IResult Forbidden() => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden");
}
