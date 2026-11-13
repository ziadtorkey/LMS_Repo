using Lms.Api.Features.Content;
using Lms.Api.Features.Enrollment;
using Lms.Api.Features.Enrollment.Groups;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Domain.Packages;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Packages;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Lms.Shared.Packages;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Packages;

/// <summary>
/// The package routes of M4b Task 8a (route table of the M4b plan): upload (see <see cref="PackageUploadEndpoint"/>), the unattached list,
/// status, delete and attach to the lesson. Every route needs the permission and the relationship to the package's ClassCourse
/// (a miss is 404, so a package id never leaks existence); a student has no <c>Packages.Upload</c> and is refused with 403.
/// </summary>
internal static class PackageEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        PackageUploadEndpoint.Map(app);
        app.MapGet(Routes.ClassCoursePackagesPattern, ListAsync).RequireAuthorization().RequirePermission(Permissions.Modules.Manage);
        app.MapGet(Routes.PackagePattern, GetAsync).RequireAuthorization().RequirePermission(Permissions.Packages.Upload);
        app.MapDelete(Routes.PackagePattern, DeleteAsync).RequireAuthorization().RequirePermission(Permissions.Packages.Upload);
        app.MapPost(Routes.PackageModulePattern, AttachAsync).RequireAuthorization().RequirePermission(Permissions.Packages.Upload);
    }

    private static async Task<IResult> ListAsync(
        Guid id, Guid? lessonKey, string? status, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var (_, problem) = await http.CheckClassCourseAsync(Permissions.Modules.Manage, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (status is not null && !PackageUploadStatuses.All.Contains(status))
        {
            return PackageSupport.Invalid("status", "invalid_value");
        }

        var query = db.Set<PackageUpload>().Where(p => p.ClassCourseId == id && p.ModuleId == null);
        if (lessonKey is { } lesson)
        {
            query = query.Where(p => p.LessonKey == lesson);
        }

        if (status is not null)
        {
            query = query.Where(p => p.Status == status);
        }

        var rows = await query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(ct);
        return Results.Ok(rows.Select(PackageSupport.ToDto).ToList());
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var found = await FindAsync(id, Permissions.Packages.Upload, http, db, ct);
        return found.Problem ?? Results.Ok(PackageSupport.ToDto(found.Upload!));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, HttpContext http, LmsDbContext db, PlacementGuard guard, PackageService packages, IModuleWriter writer, CancellationToken ct)
    {
        var found = await FindAsync(id, Permissions.Packages.Upload, http, db, ct);
        if (found.Problem is not null)
        {
            return found.Problem;
        }

        var upload = found.Upload!;
        if (await GroupEndpoints.WritableAsync(found.Course!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        if (upload.ModuleId is not null)
        {
            await writer.DeleteForRefAsync(ModuleKindOf(upload), upload.Id, ct);
        }

        await packages.DeleteAsync(upload, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AttachAsync(
        Guid id, AttachPackageModuleRequest? request, HttpContext http, LmsDbContext db, PlacementGuard guard, IModuleWriter writer,
        IClock clock, CancellationToken ct)
    {
        if (request?.Module is null)
        {
            return PackageSupport.Invalid("body", "required");
        }

        var found = await FindAsync(id, Permissions.Packages.Upload, http, db, ct);
        if (found.Problem is not null)
        {
            return found.Problem;
        }

        var upload = found.Upload!;
        var classCourse = found.Course;

        // Attaching also adds a module to the lesson: Modules.Manage on the same ClassCourse.
        var (_, modules) = await http.CheckClassCourseAsync(Permissions.Modules.Manage, upload.ClassCourseId, ct);
        if (modules is not null)
        {
            return modules;
        }

        if (upload.Status != PackageUploadStatuses.Ready)
        {
            return ApiProblems.Conflict(ProblemCodes.package_not_ready);
        }

        if (upload.ModuleId is not null)
        {
            return ApiProblems.Conflict(ProblemCodes.validation_failed, "The package is already attached to a lesson.");
        }

        if (await GroupEndpoints.WritableAsync(classCourse!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        if (!PackageGradingMethods.All.Contains(request.GradingMethod))
        {
            return PackageSupport.Invalid("gradingMethod", "invalid_value");
        }

        var kind = ModuleKindOf(upload);
        var settings = request.Module;
        if (!CompletionRules.ForActivity(kind).Contains(settings.CompletionRule))
        {
            return PackageSupport.Invalid("module.completionRule", "invalid_value");
        }

        Guid moduleId;
        try
        {
            moduleId = await writer.CreateForRefAsync(
                classCourse!.Id, upload.LessonKey, kind, upload.Id,
                new ModuleMetadata(
                    settings.TitleAr, settings.TitleEn, settings.Visibility, settings.VisibleFromUtc, settings.VisibleToUtc, settings.Target,
                    settings.GroupId, settings.CompletionRule, settings.CompletionMinScore), ct);
        }
        catch (ModuleRuleException rule)
        {
            return ApiProblems.BadRequest(rule.Code);
        }
        catch (EnrollmentRuleException rule)
        {
            return EnrollmentProblems.From(rule);
        }

        upload.AttachTo(moduleId, request.GradingMethod);
        await db.SaveChangesAsync(ct);
        var module = await db.Set<LessonModule>().FirstAsync(m => m.Id == moduleId, ct);
        var groups = await ModuleMapper.GroupsAsync(db, [module], ct);
        return Results.Created(Routes.Module(module.Id), ModuleMapper.Node(module, clock.UtcNow, groups, null));
    }

    private static string ModuleKindOf(PackageUpload upload) => upload.Kind == PackageKinds.H5p ? ModuleKinds.H5p : ModuleKinds.Scorm;

    private sealed record Found(PackageUpload? Upload, Domain.Classes.ClassCourse? Course, IResult? Problem);

    /// <summary>The upload (filtered set: another client, a sibling unit or a deleted package is 404) and then the relationship to its ClassCourse.</summary>
    private static async Task<Found> FindAsync(Guid id, string permission, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var upload = await db.Set<PackageUpload>().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (upload is null)
        {
            return new Found(null, null, ApiProblems.NotFound());
        }

        var (course, problem) = await http.CheckClassCourseAsync(permission, upload.ClassCourseId, ct);
        return problem is not null ? new Found(null, null, problem) : new Found(upload, course, null);
    }
}
