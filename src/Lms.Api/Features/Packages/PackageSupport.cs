using Lms.Domain.Classes;
using Lms.Domain.Packages;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Packages;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lms.Api.Features.Packages;

/// <summary>The checks and mapping the package routes share.</summary>
internal static class PackageSupport
{
    public static PackageUploadDto ToDto(PackageUpload u) =>
        new(u.Id, u.ClassCourseId, u.LessonKey, u.FileName, u.Kind, u.Status, u.RejectReason, u.Title, u.Version, u.MainLibrary, u.ModuleId, u.CreatedAt);

    /// <summary>403 <c>feature_disabled</c> when <c>Features.ScormContent</c> is off at the class course's unit.</summary>
    public static async Task<IResult?> FlagAsync(ClassCourse classCourse, ISettingsResolver settings, CancellationToken ct) =>
        (await settings.ResolveAsync(FeatureFlags.ScormContent, classCourse.OrgUnitPath, ct)).Value
            ? null
            : Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.feature_disabled });

    /// <summary>409 <c>package_host_not_configured</c> unless the content origin is a separate registrable domain (D56: fail closed, no app-origin fallback).</summary>
    public static IResult? HostProblem(HttpContext http, IOptions<PackageHostOptions> options) =>
        PackageHostOriginCheck.IsSeparate(options.Value.Origin, $"{http.Request.Scheme}://{http.Request.Host}")
            ? null
            : Api.Infrastructure.ApiProblems.Conflict(ProblemCodes.package_host_not_configured);

    public static Task<bool> LessonExistsAsync(LmsDbContext db, Guid classCourseId, Guid lessonKey, CancellationToken ct) =>
        (from p in db.Set<ClassLessonPlan>()
         join l in db.Set<ClassLesson>() on p.Id equals l.PlanId
         where p.ClassCourseId == classCourseId && l.Key == lessonKey
         select l.Id).AnyAsync(ct);

    public static IResult Invalid(string field, string detail) =>
        Api.Infrastructure.ApiProblems.BadRequest(ProblemCodes.validation_failed, $"{field}: {detail}");
}
