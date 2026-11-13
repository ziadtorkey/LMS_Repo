using Lms.Api.Features.Content;
using Lms.Api.Features.Enrollment;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Lms.Shared.Library;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Library;

/// <summary>
/// "Add to lesson" (FR-LIB-01): puts a library resource into a lesson of a ClassCourse as a <c>LibraryResource</c> module
/// (<c>RefId</c> = the resource) through <see cref="IModuleWriter"/>. Needs Modules.Manage on the ClassCourse (unit scope and
/// relationship rule) and Library.View of a resource that is visible at the ClassCourse's unit, so a resource of a sibling
/// school or of another client is 404.
/// </summary>
internal static class LibraryLessonEndpoints
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost(Routes.LibraryResourceAddToLessonPattern, AddAsync).RequireAuthorization().RequirePermission(Permissions.Library.View);

    private static async Task<IResult> AddAsync(
        Guid id, AddLibraryResourceToLessonRequest? request, HttpContext http, LmsDbContext db, LibraryQueries queries, IModuleWriter writer,
        IClock clock, CancellationToken ct)
    {
        if (request?.Module is null)
        {
            return LibrarySupport.Invalid("body", "required");
        }

        var (classCourse, problem) = await http.CheckClassCourseAsync(Permissions.Modules.Manage, request.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var access = await http.CheckUnitAsync(Permissions.Library.View, classCourse!.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        if (await queries.FindAtAsync(id, access.Unit!, ct) is null)
        {
            return ApiProblems.NotFound();
        }

        var settings = request.Module;
        if (!CompletionRules.ForActivity(ModuleKinds.LibraryResource).Contains(settings.CompletionRule))
        {
            return LibrarySupport.Invalid("module.completionRule", "invalid_value");
        }

        Guid moduleId;
        try
        {
            moduleId = await writer.CreateForRefAsync(
                classCourse.Id, request.LessonKey, ModuleKinds.LibraryResource, id,
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

        var module = await db.Set<LessonModule>().FirstAsync(m => m.Id == moduleId, ct);
        var groups = await ModuleMapper.GroupsAsync(db, [module], ct);
        return Results.Created(Routes.Module(module.Id), ModuleMapper.Node(module, clock.UtcNow, groups, null));
    }
}
