using Lms.Domain.Classes;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Classes;

/// <summary>A unit with live classes cannot be deleted (FR-ORG-04). Runs inside the caller scope.</summary>
public sealed class ClassesUsageCheck(LmsDbContext db) : IOrgUnitUsageCheck
{
    public async Task<string?> FindUsageAsync(OrgUnit unit, CancellationToken cancellationToken) =>
        await db.Set<Class>().AnyAsync(c => c.OrgUnitId == unit.Id, cancellationToken) ? "classes" : null;
}
