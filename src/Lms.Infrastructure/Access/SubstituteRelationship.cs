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
    /// <summary>True when <paramref name="userId"/> substitutes for the slot on <paramref name="today"/>.</summary>
    public async Task<bool> IsSubstituteOnAsync(Guid slotId, Guid userId, DateOnly today, CancellationToken ct)
    {
        var exception = await db.Set<TimetableException>()
            .Where(e => e.SlotId == slotId && e.Date == today)
            .Select(e => new { e.Kind, e.SubstituteUserId })
            .FirstOrDefaultAsync(ct);
        return exception is not null && SubstituteRule.IsSubstituteOn(exception.Kind, exception.SubstituteUserId, userId, today, today);
    }
}
