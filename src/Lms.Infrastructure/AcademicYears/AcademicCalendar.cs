using Lms.Domain.Abstractions;
using Lms.Domain.AcademicYears;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.AcademicYears;

internal sealed class AcademicCalendar(LmsDbContext db, ICurrentContext context, IAcademicYearResolver years) : IAcademicCalendar
{
    public async Task<bool> IsNonWorkingDayAsync(string orgUnitPath, DateOnly date, CancellationToken cancellationToken) =>
        (await HolidayDatesAsync(orgUnitPath, date, date, cancellationToken)).Contains(date);

    public async Task<IReadOnlySet<DateOnly>> HolidayDatesAsync(string orgUnitPath, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var dates = new HashSet<DateOnly>();
        var chain = OrgUnitPath.AncestorsAndSelf(orgUnitPath);

        // ISOLATION-BYPASS: reading the unit by exact path only to learn its organization (platform admin / jobs).
        var organizationId = context.OrganizationId
            ?? await db.Set<OrgUnit>().IgnoreQueryFilters().Where(u => u.Path == orgUnitPath)
                .Select(u => (Guid?)u.OrganizationId).FirstOrDefaultAsync(cancellationToken);
        if (organizationId is null)
        {
            return dates;
        }

        // D22 (strict): only holidays of the unit's effective years count; a unit with its own year list does not
        // inherit the replaced ancestor year's calendar.
        var (effective, _) = await years.GetEffectiveAsync(orgUnitPath, cancellationToken);
        var yearIds = effective.Select(y => y.Id).ToList();
        if (yearIds.Count == 0)
        {
            return dates;
        }

        // ISOLATION-BYPASS: holidays of ancestor units apply to descendants but lie outside the caller's own subtree
        // filter. Re-applies the client and soft-delete filters and is bound to the unit's exact ancestor paths.
        var events = await db.Set<CalendarEvent>().IgnoreQueryFilters()
            .Where(e => !e.IsDeleted && e.OrganizationId == organizationId && e.Kind == CalendarEventKind.Holiday && yearIds.Contains(e.AcademicYearId)
                && chain.Contains(e.OrgUnitPath) && e.StartDate <= to && e.EndDate >= from)
            .Select(e => new { e.StartDate, e.EndDate }).ToListAsync(cancellationToken);
        foreach (var e in events)
        {
            for (var d = e.StartDate < from ? from : e.StartDate; d <= e.EndDate && d <= to; d = d.AddDays(1))
            {
                dates.Add(d);
            }
        }

        return dates;
    }
}
