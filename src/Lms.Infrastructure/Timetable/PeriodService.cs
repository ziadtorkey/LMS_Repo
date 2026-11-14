using Lms.Domain.Organizations;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Timetable;

/// <summary>Reads and checks the periods of a unit (FR-TT-01, D58 Q4): own set first, else the nearest ancestor's set, read only.</summary>
public sealed class PeriodService(LmsDbContext db)
{
    /// <summary>The unit's own periods when it defines any, else the nearest ancestor's (with the unit that defines them).</summary>
    public async Task<(IReadOnlyList<Period> Periods, Guid? InheritedFromUnitId)> EffectiveAsync(OrgUnit unit, CancellationToken cancellationToken)
    {
        var own = await db.Set<Period>().Where(p => p.OrgUnitId == unit.Id).OrderBy(p => p.Order).ThenBy(p => p.StartTime).ToListAsync(cancellationToken);
        if (own.Count > 0)
        {
            return (own, null);
        }

        var chain = OrgUnitPath.AncestorsAndSelf(unit.Path);

        // ISOLATION-BYPASS: periods of ancestor units apply to descendants but lie outside the caller's subtree filter. Re-applies the client and
        // soft-delete filters and is bound to the exact ancestor paths of a unit the caller can see; only the deepest defining unit's rows leave.
        var inherited = await db.Set<Period>().IgnoreQueryFilters()
            .Where(p => !p.IsDeleted && p.OrganizationId == unit.OrganizationId && chain.Contains(p.OrgUnitPath))
            .ToListAsync(cancellationToken);
        var source = inherited.OrderByDescending(p => p.OrgUnitPath.Length).Select(p => p.OrgUnitId).FirstOrDefault();
        if (source == Guid.Empty)
        {
            return ([], null);
        }

        return (inherited.Where(p => p.OrgUnitId == source).OrderBy(p => p.Order).ThenBy(p => p.StartTime).ToList(), source);
    }

    /// <summary>True when the span overlaps another period of the unit's own set (the one being edited excluded).</summary>
    public async Task<bool> OverlapsAsync(Guid orgUnitId, TimeOnly start, TimeOnly end, Guid? exceptPeriodId, CancellationToken cancellationToken)
    {
        var others = await db.Set<Period>().Where(p => p.OrgUnitId == orgUnitId && p.Id != exceptPeriodId)
            .Select(p => new { p.StartTime, p.EndTime }).ToListAsync(cancellationToken);
        return others.Any(o => Period.Overlaps(start, end, o.StartTime, o.EndTime));
    }

    public Task<int> CountOwnAsync(Guid orgUnitId, CancellationToken cancellationToken) =>
        db.Set<Period>().CountAsync(p => p.OrgUnitId == orgUnitId, cancellationToken);
}
