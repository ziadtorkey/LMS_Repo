using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Timetable;

/// <summary>
/// A dated exception of a published slot (FR-TT-05, M5 Task 15): a substitute teacher, a cancelled lesson or a lesson moved to another period of the same
/// day. The base slot never changes. Unit fields are the slot's. At most one live exception per (slot, date), enforced by <see cref="ExceptionKey"/>.
/// </summary>
[Audited]
public sealed class TimetableException : UnitOwnedEntity, ILiveKeyed
{
    // The same values as Lms.Shared.Timetable.ExceptionKinds (the domain does not reference the contract project; a unit test pins them).
    public const string SubstituteKind = "Substitute";
    public const string CancelledKind = "Cancelled";
    public const string MovedKind = "Moved";

    private TimetableException()
    {
        Kind = string.Empty;
    }

    public TimetableException(TimetableSlot slot, DateOnly date, string kind, Guid? substituteUserId, Guid? newPeriodId, string? note)
    {
        PlaceLike(slot);
        SlotId = slot.Id;
        Date = date;
        Kind = kind;
        Apply(kind, substituteUserId, newPeriodId, note);
        ExceptionKey = $"{slot.Id:N}:{date:yyyyMMdd}";
    }

    public Guid SlotId { get; private set; }

    /// <summary>The local school date the exception applies to.</summary>
    public DateOnly Date { get; private set; }

    public string Kind { get; private set; }

    public Guid? SubstituteUserId { get; private set; }

    public Guid? NewPeriodId { get; private set; }

    public string? Note { get; private set; }

    /// <summary>"slotId:yyyyMMdd" while live; unique, so a second live exception for the same slot and date is refused by the database.</summary>
    public string? ExceptionKey { get; private set; }

    /// <summary>Changes the kind and its details (the slot and the date never change).</summary>
    public void Apply(string kind, Guid? substituteUserId, Guid? newPeriodId, string? note)
    {
        Kind = kind;
        SubstituteUserId = kind == SubstituteKind ? substituteUserId : null;
        NewPeriodId = kind == MovedKind ? newPeriodId : null;
        Note = note;
    }

    public void ClearLiveKeys() => ExceptionKey = null;
}
