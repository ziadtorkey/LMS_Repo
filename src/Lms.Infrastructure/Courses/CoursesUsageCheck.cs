using Lms.Domain.Courses;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Courses;

/// <summary>A unit that owns or has allocated live courses cannot be deleted (FR-ORG-04). Runs inside the caller scope.</summary>
public sealed class CoursesUsageCheck(LmsDbContext db) : IOrgUnitUsageCheck
{
    public async Task<string?> FindUsageAsync(OrgUnit unit, CancellationToken cancellationToken)
    {
        var used = await db.Set<Course>().AnyAsync(c => c.OrgUnitId == unit.Id, cancellationToken)
            || await db.Set<CourseAllocation>().AnyAsync(a => a.OrgUnitId == unit.Id, cancellationToken);
        return used ? "courses" : null;
    }
}
