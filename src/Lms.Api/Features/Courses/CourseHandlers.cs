using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Courses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Courses;

internal static class CourseHandlers
{
    public static async Task<IResult> ListAsync(
        [AsParameters] CourseQuery query, HttpContext http, LmsDbContext db, ICourseCatalog catalog, IOrgTree tree,
        ICurrentContext context, CancellationToken ct)
    {
        string? path = null;
        if (query.OrgUnitId is { } unitId)
        {
            var access = await http.CheckUnitAsync(Permissions.Courses.View, unitId, ct);
            if (access.Problem is not null)
            {
                return access.Problem;
            }

            path = access.Unit!.Path;
        }

        IReadOnlyCollection<Guid>? only = null;
        if (query.SupervisedByMe)
        {
            var userId = context.UserId;
            only = userId is null
                ? []
                : await db.Set<CourseSupervisor>().Where(s => s.UserId == userId).Select(s => s.CourseId).ToListAsync(ct);
        }

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, Paging.MaxPageSize);
        var result = await catalog.ListVisibleAsync(
            new CourseListQuery(path, query.IncludeInherited, query.Status, query.Search?.Trim(), only, query.Sort, page, size), ct);
        var items = await new CourseMapper(db, tree, context).SummariesAsync(result.Items, ct);
        return Results.Ok(new PagedResult<CourseSummaryDto>(items, result.Total, page, size));
    }

    public static async Task<IResult> GetAsync(
        Guid id, HttpContext http, LmsDbContext db, ICourseCatalog catalog, IOrgTree tree, ICurrentContext context,
        IAccessEvaluator evaluator, IRelationshipScope relationships, CancellationToken ct)
    {
        var view = await catalog.FindAsync(id, ct);
        if (view is null)
        {
            return ApiProblems.NotFound();
        }

        var canEdit = !view.IsInherited
            && (await evaluator.EvaluateAsync(Permissions.Courses.Update, view.Course.OrgUnitPath, ct)).Allowed
            && (await relationships.ForAsync(Permissions.Courses.Update, ct)).AllowsCourse(id);
        return Results.Ok(await new CourseMapper(db, tree, context).DetailAsync(view, canEdit, ct));
    }

    public static async Task<IResult> CreateAsync(
        SaveCourseRequest request, HttpContext http, LmsDbContext db, IOrgTree tree, ICurrentContext context,
        IRelationshipScope relationships, CancellationToken ct)
    {
        if (CourseValidators.Validate(request) is { } invalid)
        {
            return invalid;
        }

        if (request.AllocateToOrgUnitIds is { Count: > 0 })
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "Allocation at creation is not available yet.");
        }

        var access = await http.CheckUnitAsync(Permissions.Courses.Create, request.OwnerOrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var owner = access.Unit!;
        if (!CanOwnCourses(owner))
        {
            return ApiProblems.BadRequest(ProblemCodes.invalid_owner_unit, "A course is owned by the client root or a course-management level.");
        }

        var course = new Course(
            owner, CourseValidators.NormalizeCode(request.Code), request.NameAr, request.NameEn,
            Blank(request.DescriptionAr), Blank(request.DescriptionEn), request.Language, request.WeeklyPeriods);
        db.Set<Course>().Add(course);

        // A Supervisor (a role limited to supervised courses) who creates a course supervises it from the start.
        if (context.UserId is { } creator && !(await relationships.ForAsync(Permissions.Courses.Update, ct)).IsUnrestricted)
        {
            db.Set<CourseSupervisor>().Add(new CourseSupervisor(course, creator));
        }

        if (await TrySaveAsync(db, ct) is { } conflict)
        {
            return conflict;
        }

        var view = new CourseView(course, false);
        return Results.Created(Routes.Course(course.Id), await new CourseMapper(db, tree, context).DetailAsync(view, true, ct));
    }

    public static async Task<IResult> UpdateAsync(
        Guid id, SaveCourseRequest request, HttpContext http, LmsDbContext db, IOrgTree tree, ICurrentContext context,
        CancellationToken ct)
    {
        if (CourseValidators.Validate(request) is { } invalid)
        {
            return invalid;
        }

        var (course, problem) = await http.CheckCourseAsync(Permissions.Courses.Update, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        course!.Update(
            CourseValidators.NormalizeCode(request.Code), request.NameAr, request.NameEn,
            Blank(request.DescriptionAr), Blank(request.DescriptionEn), request.Language, request.WeeklyPeriods);

        if (request.OwnerOrgUnitId != course.OrgUnitId)
        {
            var target = await http.CheckUnitAsync(Permissions.Courses.Create, request.OwnerOrgUnitId, ct);
            if (target.Problem is not null)
            {
                return target.Problem;
            }

            if (!CanOwnCourses(target.Unit!))
            {
                return ApiProblems.BadRequest(ProblemCodes.invalid_owner_unit, "A course is owned by the client root or a course-management level.");
            }

            // The course and the rows copied from it move together, in this transaction.
            course.MoveTo(target.Unit!);
            foreach (var restriction in await db.Set<CourseRestriction>().Where(r => r.CourseId == id).ToListAsync(ct))
            {
                restriction.MoveTo(target.Unit!);
            }

            foreach (var supervisor in await db.Set<CourseSupervisor>().Where(s => s.CourseId == id).ToListAsync(ct))
            {
                supervisor.MoveTo(target.Unit!);
            }
        }

        if (await TrySaveAsync(db, ct) is { } conflict)
        {
            return conflict;
        }

        return Results.Ok(await new CourseMapper(db, tree, context).DetailAsync(new CourseView(course, false), true, ct));
    }

    public static async Task<IResult> SetStatusAsync(
        Guid id, SetCourseStatusRequest request, HttpContext http, LmsDbContext db, IOrgTree tree, ICurrentContext context,
        CancellationToken ct)
    {
        if (CourseValidators.Validate(request) is { } invalid)
        {
            return invalid;
        }

        var (course, problem) = await http.CheckCourseAsync(Permissions.Courses.Update, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        course!.SetStatus(request.Status);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await new CourseMapper(db, tree, context).DetailAsync(new CourseView(course, false), true, ct));
    }

    public static async Task<IResult> DeleteAsync(Guid id, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var (course, problem) = await http.CheckCourseAsync(Permissions.Courses.Delete, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await db.Set<CourseAllocation>().AnyAsync(a => a.CourseId == id, ct)
            || await db.Set<ClassCourse>().AnyAsync(c => c.CourseId == id, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.course_in_use, "The course is allocated or used by a class.");
        }

        db.Set<CourseRestriction>().RemoveRange(await db.Set<CourseRestriction>().Where(r => r.CourseId == id).ToListAsync(ct));
        db.Set<CourseSupervisor>().RemoveRange(await db.Set<CourseSupervisor>().Where(s => s.CourseId == id).ToListAsync(ct));
        db.Set<Course>().Remove(course!);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Q1: the client root or a unit flagged as a course-management level owns courses.</summary>
    internal static bool CanOwnCourses(OrgUnit unit) => unit.ParentId is null || unit.IsCourseManagementLevel;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Saves; a duplicate live code of the client (the live-key unique index) answers 409.</summary>
    private static async Task<IResult?> TrySaveAsync(LmsDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ApiProblems.Conflict(ProblemCodes.course_code_taken, "Another live course of this client uses the code.");
        }
    }
}
