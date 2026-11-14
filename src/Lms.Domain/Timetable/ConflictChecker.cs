namespace Lms.Domain.Timetable;

/// <summary>The fields of a slot that decide a clash. The room is never one of them (D60).</summary>
public sealed record SlotPlace(Guid SlotId, Guid ClassId, Guid TeacherUserId, DayOfWeek Day, Guid PeriodId);

/// <summary>A clash of a candidate slot with another slot of the term.</summary>
public sealed record SlotClash(string Kind, Guid SlotId);

/// <summary>FR-TT-03: a teacher in two places, or a class with two lessons, on the same day and period of a term. No room check (D60).</summary>
public static class ConflictChecker
{
    public const string TeacherDoubleBooked = "TeacherDoubleBooked";
    public const string ClassDoubleBooked = "ClassDoubleBooked";

    /// <summary>The clashes of <paramref name="candidate"/> with <paramref name="others"/> (the candidate itself, matched by id, is ignored).</summary>
    public static IReadOnlyList<SlotClash> Find(SlotPlace candidate, IEnumerable<SlotPlace> others)
    {
        var clashes = new List<SlotClash>();
        foreach (var other in others)
        {
            if (other.SlotId == candidate.SlotId || other.Day != candidate.Day || other.PeriodId != candidate.PeriodId)
            {
                continue;
            }

            if (other.TeacherUserId == candidate.TeacherUserId)
            {
                clashes.Add(new SlotClash(TeacherDoubleBooked, other.SlotId));
            }

            if (other.ClassId == candidate.ClassId)
            {
                clashes.Add(new SlotClash(ClassDoubleBooked, other.SlotId));
            }
        }

        return clashes;
    }
}
