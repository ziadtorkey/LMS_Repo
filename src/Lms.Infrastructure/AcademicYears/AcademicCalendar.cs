using Lms.Domain.Abstractions;
using Lms.Domain.AcademicYears;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.AcademicYears;

internal sealed class AcademicCalendar(LmsDbContext db, ICurrentContext context) : IAcademicCalendar
{
    public async Task<bool> IsNonWorkingDayAsync(string orgUnitPath, DateOnly date, CancellationToken cancellationToken)
    {
        var chain = OrgUnitPath.AncestorsAndSelf(orgUnitPath);

        // ISOLATION-BYPASS: reading the unit by exact path only to learn its organization (platform admin / jobs).
        var organizationId = context.OrganizationId
            ?? await db.Set<OrgUnit>().IgnoreQueryFilters().Where(u => u.Path == orgUnitPath)
                .Select(u => (Guid?)u.OrganizationId).FirstOrDefaultAsync(cancellationToken);
        if (organizationId is null)
        {
            return false;
        }

        // ISOLATION-BYPASS: holidays of ancestor units apply to descendants but lie outside the caller's own subtree
        // filter. Re-applies the client and soft-delete filters and is bound to the unit's exact ancestor paths.
        return await db.Set<CalendarEvent>().IgnoreQueryFilters().AnyAsync(
            e => !e.IsDeleted && e.OrganizationId == organizationId && e.Kind == CalendarEventKind.Holiday && chain.Contains(e.OrgUnitPath)
                && e.StartDate <= date && date <= e.EndDate,
            cancellationToken);
    }
}
