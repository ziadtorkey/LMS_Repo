using Lms.Api.Infrastructure;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Lms.Shared.Courses;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Classes;

/// <summary>GET /api/classes/matrix: classes of a unit (and below) and year against the courses available there.</summary>
public sealed class MatrixQuery(
    LmsDbContext db, ICourseCatalog catalog, ClassMapper classMapper, ClassLookups lookups, IRelationshipScope relationships)
{
    private const int MaxClasses = 200;

    public async Task<IResult> GetAsync(Guid orgUnitId, Guid yearId, HttpContext http, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Classes.View, orgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        var path = unit.Path;
        var query = db.Set<Class>().Where(c => c.OrgUnitPath.StartsWith(path) && c.AcademicYearId == yearId);
        var filter = await relationships.ForAsync(Permissions.Classes.View, ct);
        if (!filter.IsUnrestricted)
        {
            var allowed = filter.ClassIds;
            query = query.Where(c => allowed.Contains(c.Id));
        }

        var classes = await query.OrderBy(c => c.Code).Take(MaxClasses + 1).ToListAsync(ct);
        if (classes.Count > MaxClasses)
        {
            return ClassValidators.TooMany(nameof(orgUnitId), MaxClasses);
        }

        var classIds = classes.Select(c => c.Id).ToList();
        var cells = await db.Set<ClassCourse>().Where(c => classIds.Contains(c.ClassId)).ToListAsync(ct);
        if (!filter.IsUnrestricted)
        {
            cells = cells.Where(c => filter.AllowsClassCourse(c.Id)).ToList();
        }

        var available = await catalog.AvailableAtAsync(unit, ct);
        var courses = new Dictionary<Guid, (Course Course, bool Inherited)>();
        foreach (var a in available)
        {
            courses[a.Course.Id] = (a.Course, a.IsInherited);
        }

        var missing = cells.Select(c => c.CourseId).Where(id => !courses.ContainsKey(id)).Distinct().ToList();
        foreach (var (id, course) in await lookups.CoursesAsync(unit.OrganizationId, missing, ct))
        {
            courses[id] = (course, !course.OrgUnitPath.StartsWith(path, StringComparison.Ordinal));
        }

        var owners = await lookups.UnitNamesAsync(unit.OrganizationId, courses.Values.Select(c => c.Course.OrgUnitId).Distinct().ToList(), ct);
        var courseIds = courses.Keys.ToList();
        var allocations = await db.Set<CourseAllocation>().Where(a => courseIds.Contains(a.CourseId))
            .GroupBy(a => a.CourseId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, ct);
        var courseDtos = courses.Values.OrderBy(c => c.Course.Code).Select(c =>
        {
            owners.TryGetValue(c.Course.OrgUnitId, out var owner);
            return new CourseSummaryDto(
                c.Course.Id, c.Course.Code, c.Course.NameAr, c.Course.NameEn, c.Course.Language, c.Course.WeeklyPeriods,
                c.Course.OrgUnitId, owner.NameAr ?? string.Empty, owner.NameEn ?? string.Empty, c.Course.Status, c.Inherited,
                allocations.GetValueOrDefault(c.Course.Id), false);
        }).ToList();

        var cellDtos = cells.Select(c => new MatrixCellDto(c.ClassId, c.CourseId, c.Id, c.IsOptional)).ToList();
        return Results.Ok(new DistributionMatrixDto(await classMapper.ToDtosAsync(classes, ct), courseDtos, cellDtos));
    }
}
