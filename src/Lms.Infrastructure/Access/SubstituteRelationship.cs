using Lms.Domain.Timetable;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Access;

/// <summary>
/// The substitute relationship of M5 plan L15: a substitute is the teacher of record for <b>one session on one date</b> and nothing else. It holds when
/// a live <c>Substitute</c> exception of the slot names the user and the date is today (the client-local date, passed in by the caller). Task 17 consumes it
/// to let a substitute take attendance for that one session; any other slot, date or permission is a relationship miss (404).
/// </summary>
public sealed class SubstituteRelationship(LmsDbContext db)
{
    /// <summary>
    /// True when <paramref name="userId"/> substitutes for the slot on <paramref name="today"/>. The covering teacher is assigned in their own class or school, so the
    /// slot and its exception can sit outside their unit scope: the exception is read across the client, and only this yes or no leaves (never a name or a row).
    /// </summary>
    public async Task<bool> IsSubstituteOnAsync(Guid organizationId, Guid slotId, Guid userId, DateOnly today, CancellationToken ct)
    {
        // ISOLATION-BYPASS: a substitute teaches in another class or school of the client, outside their subtree scope; limited to the client, one live slot, one
        // date and the caller's own user id; only a yes or no leaves.
        var exception = await db.Set<TimetableException>().IgnoreQueryFilters()
            .Where(e => !e.IsDeleted && e.OrganizationId == organizationId && e.SlotId == slotId && e.Date == today && e.SubstituteUserId == userId)
            .Select(e => new { e.Kind, e.SubstituteUserId })
            .FirstOrDefaultAsync(ct);
        return exception is not null && SubstituteRule.IsSubstituteOn(exception.Kind, exception.SubstituteUserId, userId, today, today);
    }

    /// <summary>The (slot, date) pairs a user substitutes on inside a date range, across the client (the same disclosure as <see cref="IsSubstituteOnAsync"/>: ids of the caller's own substitutions).</summary>
    public async Task<IReadOnlyList<(Guid SlotId, DateOnly Date)>> SubstitutionsAsync(Guid organizationId, Guid userId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        // ISOLATION-BYPASS: same reason as IsSubstituteOnAsync; limited to the client, live rows, the caller's own user id and a date range.
        var rows = await db.Set<TimetableException>().IgnoreQueryFilters()
            .Where(e => !e.IsDeleted && e.OrganizationId == organizationId && e.Kind == TimetableException.SubstituteKind && e.SubstituteUserId == userId
                        && e.Date >= from && e.Date <= to)
            .Select(e => new { e.SlotId, e.Date }).ToListAsync(ct);
        return rows.Select(r => (r.SlotId, r.Date)).ToList();
    }
}
