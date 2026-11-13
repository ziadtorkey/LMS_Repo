using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Courses;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Courses;

/// <summary>Builds <see cref="CourseAllocationDto"/>s (scoped service).</summary>
internal sealed class AllocationMapper(LmsDbContext db, IOrgTree tree, ICourseCatalog catalog)
{
    /// <summary>Allocations at the unit or one of its ancestors; <c>IsInherited</c> = allocated above the viewed unit.</summary>
    public async Task<IReadOnlyList<CourseAllocationDto>> ToDtosAsync(
        IReadOnlyList<(CourseAllocation Allocation, CourseView Course)> rows, OrgUnit viewed, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var names = (await tree.GetAncestorsAndSelfAsync(viewed.Path, viewed.OrganizationId, ct)).ToDictionary(u => u.Id);
        var result = new List<CourseAllocationDto>();
        foreach (var (allocation, course) in rows)
        {
            names.TryGetValue(allocation.OrgUnitId, out var unit);
            result.Add(new CourseAllocationDto(
                allocation.Id, course.Course.Id, course.Course.Code, course.Course.NameAr, course.Course.NameEn, allocation.OrgUnitId,
                unit?.NameAr ?? string.Empty, unit?.NameEn ?? string.Empty, allocation.OrgUnitId != viewed.Id,
                await ClassCoursesAsync(allocation, ct)));
        }

        return result;
    }

    /// <summary>Allocations strictly below a unit (units inside the caller scope, so names come through the filters).</summary>
    public async Task<IReadOnlyList<CourseAllocationDto>> BelowAsync(IReadOnlyList<CourseAllocation> allocations, CancellationToken ct)
    {
        var unitIds = allocations.Select(a => a.OrgUnitId).Distinct().ToList();
        var units = await db.OrgUnits.Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var result = new List<CourseAllocationDto>();
        foreach (var allocation in allocations)
        {
            var course = (await catalog.FindAsync(allocation.CourseId, ct))?.Course;
            if (course is null)
            {
                continue;
            }

            units.TryGetValue(allocation.OrgUnitId, out var unit);
            result.Add(new CourseAllocationDto(
                allocation.Id, course.Id, course.Code, course.NameAr, course.NameEn, allocation.OrgUnitId,
                unit?.NameAr ?? string.Empty, unit?.NameEn ?? string.Empty, false, await ClassCoursesAsync(allocation, ct)));
        }

        return result;
    }

    private Task<int> ClassCoursesAsync(CourseAllocation allocation, CancellationToken ct) =>
        db.Set<ClassCourse>().CountAsync(cc => cc.CourseId == allocation.CourseId && cc.OrgUnitPath.StartsWith(allocation.OrgUnitPath), ct);
}
