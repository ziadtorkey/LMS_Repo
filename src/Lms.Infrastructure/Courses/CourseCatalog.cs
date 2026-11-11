using Lms.Domain.Abstractions;
using Lms.Domain.Courses;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Courses;

/// <summary>
/// Reads courses across the ancestor chain. A course owned by an ancestor of the caller scope is invisible to the
/// normal subtree filter, so these queries bypass it and re-apply the client, soft-delete and visibility rules by hand.
/// </summary>
public sealed class CourseCatalog(LmsDbContext db, ICurrentContext context) : ICourseCatalog
{
    public async Task<CourseView?> FindAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        if (!HasContext())
        {
            return null;
        }

        // ISOLATION-BYPASS: an ancestor-owned course lies outside the caller subtree. Client and soft-delete are
        // re-applied; visibility is "owner inside the caller scope or owner an ancestor of it" (no siblings).
        var course = await db.Set<Course>().IgnoreQueryFilters()
            .Where(c => c.Id == courseId && !c.IsDeleted && (context.IsPlatformAdmin || c.OrganizationId == context.OrganizationId))
            .SingleOrDefaultAsync(cancellationToken);

        if (course is null || !IsVisible(course.OrgUnitPath))
        {
            return null;
        }

        return new CourseView(course, IsInherited(course.OrgUnitPath));
    }

    public async Task<bool> IsAvailableAtAsync(Guid courseId, OrgUnit unit, CancellationToken cancellationToken = default)
    {
        var paths = OrgUnitPath.AncestorsAndSelf(unit.Path);

        // ISOLATION-BYPASS: allocations sit on ancestors of the unit, outside the caller subtree. Client and
        // soft-delete are re-applied; only the unit own client is read.
        return await db.Set<CourseAllocation>().IgnoreQueryFilters()
            .AnyAsync(a => a.CourseId == courseId && !a.IsDeleted && a.OrganizationId == unit.OrganizationId
                && paths.Contains(a.OrgUnitPath), cancellationToken);
    }

    public async Task<IReadOnlyList<AvailableCourse>> AvailableAtAsync(OrgUnit unit, CancellationToken cancellationToken = default)
    {
        var paths = OrgUnitPath.AncestorsAndSelf(unit.Path);

        // ISOLATION-BYPASS: same reasoning as IsAvailableAtAsync; the joined courses are live rows of the same client.
        var rows = await (
            from a in db.Set<CourseAllocation>().IgnoreQueryFilters()
            join c in db.Set<Course>().IgnoreQueryFilters() on a.CourseId equals c.Id
            where !a.IsDeleted && !c.IsDeleted && a.OrganizationId == unit.OrganizationId
                && c.OrganizationId == unit.OrganizationId && paths.Contains(a.OrgUnitPath)
            orderby c.Code
            select new { Allocation = a, Course = c }).ToListAsync(cancellationToken);

        return rows.Select(r => new AvailableCourse(r.Allocation, r.Course, r.Allocation.OrgUnitId != unit.Id)).ToList();
    }

    public async Task<CoursePage> ListVisibleAsync(CourseListQuery query, CancellationToken cancellationToken = default)
    {
        if (!HasContext())
        {
            return new CoursePage([], 0);
        }

        // ISOLATION-BYPASS: lists ancestor-owned courses too; client and soft-delete are re-applied and visibility is
        // "owner inside the caller scope or an ancestor of it".
        var courses = db.Set<Course>().IgnoreQueryFilters()
            .Where(c => !c.IsDeleted && (context.IsPlatformAdmin || c.OrganizationId == context.OrganizationId));

        if (!context.IsPlatformAdmin)
        {
            var scope = context.ScopePath!;
            var scopeAncestors = OrgUnitPath.AncestorsAndSelf(scope);
            courses = courses.Where(c => c.OrgUnitPath.StartsWith(scope) || scopeAncestors.Contains(c.OrgUnitPath));
        }

        if (query.OrgUnitPath is { } path)
        {
            var ancestors = OrgUnitPath.AncestorsAndSelf(path);
            courses = query.IncludeInherited
                ? courses.Where(c => c.OrgUnitPath.StartsWith(path) || ancestors.Contains(c.OrgUnitPath))
                : courses.Where(c => c.OrgUnitPath.StartsWith(path));
        }
        else if (!query.IncludeInherited && context.ScopePath is { } own)
        {
            courses = courses.Where(c => c.OrgUnitPath.StartsWith(own));
        }

        if (query.Status is { Length: > 0 } status)
        {
            courses = courses.Where(c => c.Status == status);
        }

        if (query.Search is { Length: > 0 } search)
        {
            courses = courses.Where(c => c.Code.Contains(search) || c.NameAr.Contains(search) || c.NameEn.Contains(search));
        }

        if (query.OnlyCourseIds is { } only)
        {
            courses = courses.Where(c => only.Contains(c.Id));
        }

        courses = query.Sort switch
        {
            "-code" => courses.OrderByDescending(c => c.Code),
            "name" => courses.OrderBy(c => c.NameEn).ThenBy(c => c.Code),
            "-name" => courses.OrderByDescending(c => c.NameEn).ThenBy(c => c.Code),
            _ => courses.OrderBy(c => c.Code),
        };

        var total = await courses.CountAsync(cancellationToken);
        var page = await courses.Skip(Math.Max(0, query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken);
        return new CoursePage(page.Select(c => new CourseView(c, IsInherited(c.OrgUnitPath))).ToList(), total);
    }

    // A malformed scope (empty, "/", not slash-delimited) is treated as no scope, like the M1 query filters do.
    private bool HasContext() =>
        context.IsPlatformAdmin
        || (context.OrganizationId is not null && context.ScopePath is { Length: > 1 } scope && scope[0] == '/' && scope[^1] == '/');

    private bool IsVisible(string ownerPath) =>
        context.IsPlatformAdmin
        || (context.ScopePath is { } scope && (OrgUnitPath.IsWithin(ownerPath, scope) || OrgUnitPath.IsWithin(scope, ownerPath)));

    private bool IsInherited(string ownerPath) =>
        !context.IsPlatformAdmin && context.ScopePath is { } scope && !OrgUnitPath.IsWithin(ownerPath, scope);
}
