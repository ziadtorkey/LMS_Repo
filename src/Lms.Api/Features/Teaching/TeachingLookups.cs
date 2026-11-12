using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Teaching;

/// <summary>
/// Yes/no questions about teachers that must look beyond the caller's subtree (the Teacher assignment and the
/// eligibility may sit at an ancestor unit). Every query is limited to one client and live rows; only booleans leave.
/// </summary>
public sealed class TeachingLookups(LmsDbContext db, IClock clock)
{
    /// <summary>The user holds a valid Teacher assignment at the unit, above it or below it.</summary>
    public async Task<bool> IsTeacherNearAsync(Guid organizationId, Guid userId, string unitPath, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);
        // ISOLATION-BYPASS: the Teacher assignment may sit at an ancestor of the unit, above the caller's subtree; limited
        // to the client, live rows and the one user; only a yes/no leaves.
        var assignments = await (from a in db.RoleAssignments.IgnoreQueryFilters()
                                 join r in db.Roles on a.RoleId equals r.Id
                                 where !a.IsDeleted && a.OrganizationId == organizationId && a.UserId == userId
                                     && r.BuiltInKey == BuiltInRoles.Teacher
                                 select a).ToListAsync(ct);
        return assignments.Any(a => a.IsValidOn(today)
            && (unitPath.StartsWith(a.OrgUnitPath, StringComparison.Ordinal) || a.OrgUnitPath.StartsWith(unitPath, StringComparison.Ordinal)));
    }

    /// <summary>The teacher may teach the course at the class unit: an eligibility sits at that unit or an ancestor.</summary>
    public async Task<bool> IsEligibleAsync(Guid organizationId, Guid userId, Guid courseId, string classUnitPath, CancellationToken ct)
    {
        // ISOLATION-BYPASS: the eligibility may be granted at an ancestor of the class unit, above the caller's subtree;
        // limited to the client, live rows, one user and one course; only a yes/no leaves.
        var rows = await db.Set<TeacherCourseEligibility>().IgnoreQueryFilters()
            .Where(e => !e.IsDeleted && e.OrganizationId == organizationId && e.UserId == userId && e.CourseId == courseId)
            .Select(e => e.OrgUnitPath).ToListAsync(ct);
        return rows.Any(path => classUnitPath.StartsWith(path, StringComparison.Ordinal));
    }

    /// <summary>Eligibilities that apply to a class at the unit: granted at that unit or any ancestor (the assign rule).</summary>
    public IQueryable<TeacherCourseEligibility> EligibilitiesAppliedAt(Guid organizationId, string unitPath)
    {
        // "/a/b/c/" -> "/a/", "/a/b/", "/a/b/c/": a path is an ancestor-or-self when it is a prefix ending at a separator.
        var segments = unitPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var paths = new List<string>(segments.Length);
        var current = "/";
        foreach (var segment in segments)
        {
            current += segment + "/";
            paths.Add(current);
        }

        // ISOLATION-BYPASS: the eligibility may be granted at an ancestor of the class unit, above the caller's subtree;
        // limited to the client, live rows and the ancestor-or-self paths of one unit; the caller adds user filters.
        return db.Set<TeacherCourseEligibility>().IgnoreQueryFilters()
            .Where(e => !e.IsDeleted && e.OrganizationId == organizationId && paths.Contains(e.OrgUnitPath));
    }
}
