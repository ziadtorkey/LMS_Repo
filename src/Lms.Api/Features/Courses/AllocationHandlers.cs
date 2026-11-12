using Lms.Api.Infrastructure;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Courses;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Courses;

internal static class AllocationHandlers
{
    public const int MaxCoursesPerRequest = 100;

    /// <summary>A course can be allocated at a unit when it is Active and its owner is the unit or one of its ancestors.</summary>
    public static bool IsAllocatable(Course course, OrgUnit unit) =>
        course.Status == CourseStatuses.Active && OrgUnitPath.AncestorsAndSelf(unit.Path).Contains(course.OrgUnitPath);

    public static async Task<IResult> AllocateAsync(
        AllocateCoursesRequest request, HttpContext http, LmsDbContext db, ICourseCatalog catalog, IRelationshipScope relationships,
        AllocationMapper mapper, CancellationToken ct)
    {
        var ids = request.CourseIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "CourseIds is required.");
        }

        if (ids.Count > MaxCoursesPerRequest)
        {
            return ApiProblems.BadRequest(ProblemCodes.too_many_items, "At most 100 courses per request.");
        }

        var access = await http.CheckUnitAsync(Permissions.Courses.Allocate, request.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        var created = await AddAllocationsAsync(db, catalog, relationships, unit, ids, ct);
        if (created.Problem is not null)
        {
            return created.Problem;
        }

        if (await SaveAsync(db, ct) is { } conflict)
        {
            return conflict;
        }

        return Results.Created(Routes.CourseAllocations, await mapper.ToDtosAsync(created.Rows, unit, ct));
    }

    /// <summary>
    /// Validates every course and stages one allocation per course at the unit (nothing is saved here): all or nothing.
    /// Used by POST /api/course-allocations and by allocate-at-creation.
    /// </summary>
    public static async Task<(IReadOnlyList<(CourseAllocation Allocation, CourseView Course)> Rows, IResult? Problem)> AddAllocationsAsync(
        LmsDbContext db, ICourseCatalog catalog, IRelationshipScope relationships, OrgUnit unit, IReadOnlyList<Guid> courseIds,
        CancellationToken ct, IReadOnlyDictionary<Guid, Course>? justCreated = null)
    {
        var filter = await relationships.ForAsync(Permissions.Courses.Allocate, ct);
        var rows = new List<(CourseAllocation, CourseView)>();
        foreach (var id in courseIds)
        {
            Course? course;
            var isNew = justCreated is not null && justCreated.TryGetValue(id, out course);
            course = isNew ? justCreated![id] : (await catalog.FindAsync(id, ct))?.Course;
            if (course is null || (!isNew && !filter.AllowsCourse(id)))
            {
                return ([], ApiProblems.NotFound());
            }

            if (!IsAllocatable(course, unit))
            {
                return ([], ApiProblems.BadRequest(ProblemCodes.course_not_available, "The course is inactive or not owned at or above this unit."));
            }

            if (await db.Set<CourseAllocation>().AnyAsync(a => a.OrgUnitId == unit.Id && a.CourseId == id, ct))
            {
                return ([], ApiProblems.Conflict(ProblemCodes.allocation_exists, "The course is already allocated at this unit."));
            }

            var allocation = new CourseAllocation(course, unit);
            db.Set<CourseAllocation>().Add(allocation);
            rows.Add((allocation, new CourseView(course, course.OrgUnitId != unit.Id)));
        }

        return (rows, null);
    }

    public static async Task<IResult> RemoveAsync(
        Guid id, HttpContext http, LmsDbContext db, ICourseCatalog catalog, IRelationshipScope relationships, CancellationToken ct)
    {
        var allocation = await db.Set<CourseAllocation>().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (allocation is null)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.Courses.Allocate, allocation.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        if (!(await relationships.ForAsync(Permissions.Courses.Allocate, ct)).AllowsCourse(allocation.CourseId))
        {
            return ApiProblems.NotFound();
        }

        // In use: a class at or below this unit teaches the course and no other allocation at or above that class unit covers it.
        var unitPath = allocation.OrgUnitPath;
        var users = await db.Set<ClassCourse>()
            .Where(cc => cc.CourseId == allocation.CourseId && cc.OrgUnitPath.StartsWith(unitPath))
            .Select(cc => cc.OrgUnitPath).Distinct().ToListAsync(ct);
        foreach (var classPath in users)
        {
            if (!await catalog.IsAvailableAtPathAsync(allocation.OrganizationId, classPath, allocation.CourseId, allocation.Id, ct))
            {
                return ApiProblems.Conflict(ProblemCodes.allocation_in_use, "A class at or below this unit uses the course.");
            }
        }

        db.Set<CourseAllocation>().Remove(allocation);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    public static async Task<IResult> UnitCoursesAsync(
        Guid id, HttpContext http, LmsDbContext db, ICourseCatalog catalog, AllocationMapper mapper, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Courses.View, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        var available = await catalog.AvailableAtAsync(unit, ct);
        var below = await db.Set<CourseAllocation>()
            .Where(a => a.OrgUnitPath.StartsWith(unit.Path) && a.OrgUnitId != unit.Id).OrderBy(a => a.OrgUnitPath).ToListAsync(ct);
        return Results.Ok(new UnitCoursesDto(
            await mapper.ToDtosAsync(available.Select(a => (a.Allocation, new CourseView(a.Course, a.IsInherited))).ToList(), unit, ct),
            await mapper.BelowAsync(below, ct)));
    }

    /// <summary>Saves; a duplicate (unit, course) caught by the live-key index answers 409.</summary>
    public static async Task<IResult?> SaveAsync(LmsDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase) == true)
        {
            return ApiProblems.Conflict(ProblemCodes.allocation_exists, "The course is already allocated at this unit.");
        }
    }
}
