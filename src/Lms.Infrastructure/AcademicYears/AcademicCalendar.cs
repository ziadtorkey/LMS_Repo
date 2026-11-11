using Lms.Domain.Abstractions;
using Lms.Domain.AcademicYears;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.AcademicYears;

internal sealed class AcademicCalendar(LmsDbContext db, ICurrentContext context, IAcademicYearResolver years) : IAcademicCalendar
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

        // D22 (strict): only holidays of the unit's effective years count; a unit with its own year list does not
        // inherit the replaced ancestor year's calendar.
        var (effective, _) = await years.GetEffectiveAsync(orgUnitPath, cancellationToken);
        var yearIds = effective.Select(y => y.Id).ToList();
        if (yearIds.Count == 0)
        {
            return false;
        }

        // ISOLATION-BYPASS: holidays of ancestor units apply to descendants but lie outside the caller's own subtree
        // filter. Re-applies the client and soft-delete filters and is bound to the unit's exact ancestor paths.
        return await db.Set<CalendarEvent>().IgnoreQueryFilters().AnyAsync(
            e => !e.IsDeleted && e.OrganizationId == organizationId && e.Kind == CalendarEventKind.Holiday && yearIds.Contains(e.AcademicYearId) && chain.Contains(e.OrgUnitPath)
                && e.StartDate <= date && date <= e.EndDate,
            cancellationToken);
    }
}
