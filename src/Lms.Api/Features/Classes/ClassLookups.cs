using Lms.Domain.AcademicYears;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Classes;

/// <summary>
/// The reads of the class endpoints that must look beyond the caller's subtree. Every query is limited to one client
/// and to live rows; none of them returns rows to the caller (names and flags only).
/// </summary>
public sealed class ClassLookups(LmsDbContext db, IClock clock)
{
    /// <summary>Years by id: a year can be inherited from an ancestor unit outside the caller's own subtree.</summary>
    public async Task<Dictionary<Guid, AcademicYear>> YearsAsync(Guid organizationId, IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        // ISOLATION-BYPASS: years are defined at ancestor units the caller may not see; limited to the client, live rows.
        await db.Set<AcademicYear>().IgnoreQueryFilters()
            .Where(y => !y.IsDeleted && y.OrganizationId == organizationId && ids.Contains(y.Id))
            .ToDictionaryAsync(y => y.Id, ct);

    /// <summary>Courses by id (a course can be owned by an ancestor unit above the caller). Names and codes only leave.</summary>
    public async Task<Dictionary<Guid, Lms.Domain.Courses.Course>> CoursesAsync(
        Guid organizationId, IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        // ISOLATION-BYPASS: inherited courses are owned above the caller's subtree; limited to the client, live rows and
        // ids the caller already reaches through a class course or an allocation at their unit.
        await db.Set<Lms.Domain.Courses.Course>().IgnoreQueryFilters()
            .Where(c => !c.IsDeleted && c.OrganizationId == organizationId && ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

    /// <summary>Unit names by id, for the owner of inherited courses.</summary>
    public async Task<Dictionary<Guid, (string NameAr, string NameEn)>> UnitNamesAsync(
        Guid organizationId, IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        // ISOLATION-BYPASS: the owner of an inherited course is an ancestor unit; only its name leaves, limited to the
        // client and live rows.
        (await db.OrgUnits.IgnoreQueryFilters()
            .Where(u => !u.IsDeleted && u.OrganizationId == organizationId && ids.Contains(u.Id))
            .Select(u => new { u.Id, u.NameAr, u.NameEn }).ToListAsync(ct))
        .ToDictionary(u => u.Id, u => (u.NameAr, u.NameEn));

    /// <summary>Which of the upper-cased codes are already taken by a live class of the year (unique per client and year).</summary>
    public async Task<List<string>> TakenCodesAsync(
        Guid organizationId, Guid yearId, IReadOnlyCollection<string> codes, Guid? exceptClassId, CancellationToken ct) =>
        // ISOLATION-BYPASS: code uniqueness is per client and year, so a class in a sibling unit still collides; only the
        // colliding code is returned, limited to the client, live rows.
        await db.Set<Class>().IgnoreQueryFilters()
            .Where(c => !c.IsDeleted && c.OrganizationId == organizationId && c.AcademicYearId == yearId
                && c.CodeKey != null && codes.Contains(c.CodeKey) && c.Id != exceptClassId)
            .Select(c => c.Code.ToUpper())
            .ToListAsync(ct);

    /// <summary>A homeroom teacher holds a current Teacher-role assignment at the class unit or one of its ancestors.</summary>
    public async Task<bool> IsTeacherAtAsync(Guid organizationId, Guid userId, string unitPath, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);
        // ISOLATION-BYPASS: the Teacher assignment may sit at an ancestor of the class unit, above the caller's subtree;
        // limited to the client, live rows and the one user; only a yes/no leaves.
        var assignments = await (from a in db.RoleAssignments.IgnoreQueryFilters()
                                 join r in db.Roles on a.RoleId equals r.Id
                                 where !a.IsDeleted && a.OrganizationId == organizationId && a.UserId == userId
                                     && r.BuiltInKey == BuiltInRoles.Teacher
                                 select a).ToListAsync(ct);
        return assignments.Any(a => a.IsValidOn(today) && unitPath.StartsWith(a.OrgUnitPath, StringComparison.Ordinal));
    }
}
