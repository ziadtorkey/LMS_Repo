using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.People;

/// <summary>A unit that is the home of live students or staff cannot be deleted (FR-ORG-04). Runs inside the caller's scope.</summary>
public sealed class PeopleUsageCheck(LmsDbContext db) : IOrgUnitUsageCheck
{
    public async Task<string?> FindUsageAsync(OrgUnit unit, CancellationToken cancellationToken)
    {
        if (await db.StudentProfiles.AnyAsync(s => s.OrgUnitId == unit.Id, cancellationToken))
        {
            return "students";
        }

        return await db.StaffProfiles.AnyAsync(s => s.OrgUnitId == unit.Id, cancellationToken) ? "staff" : null;
    }
}
