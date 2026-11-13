namespace Lms.Client.Services.Timetable;

/// <summary>One lesson on the weekly grid. <see cref="Day"/> is a <see cref="DayOfWeek"/> value, <see cref="Period"/> a 1-based period number.</summary>
public sealed record SpikeSlot(Guid Id, string Course, string Teacher, string? Room, int Day, int Period, bool InThisClass = true);

public enum SpikeConflict { None, ClassDoubleBooked, TeacherDoubleBooked }

/// <summary>What the (mock) server answered to a move: refused in Block mode, saved with warnings in Warn mode.</summary>
public sealed record SpikeMoveResult(bool Saved, IReadOnlyList<(SpikeConflict Kind, SpikeSlot Other)> Conflicts);

/// <summary>
/// Mock of the timetable data and of the server's conflict answer for spike S3 (M5 Task 31). The real conflict check is the server's
/// (#616 section 3.4: the client never predicts conflicts); this stand-in exists so the interaction can be built before the wave 2
/// contract lands. It is replaced by <c>FakeTimetableApi</c> in Task 31.
/// </summary>
public sealed class SpikeTimetable
{
    private readonly List<SpikeSlot> _slots;

    public SpikeTimetable(bool blockOnConflict = true)
    {
        BlockOnConflict = blockOnConflict;
        _slots =
        [
            new(Guid.Parse("00000000-0000-0000-0000-000000000001"), "Mathematics", "A. Saleh", "101", (int)DayOfWeek.Sunday, 1),
            new(Guid.Parse("00000000-0000-0000-0000-000000000002"), "Arabic", "M. Nasser", "101", (int)DayOfWeek.Sunday, 2),
            new(Guid.Parse("00000000-0000-0000-0000-000000000003"), "Science", "L. Omar", "Lab 2", (int)DayOfWeek.Monday, 1),
            new(Guid.Parse("00000000-0000-0000-0000-000000000004"), "English", "R. Haddad", null, (int)DayOfWeek.Tuesday, 3),
            new(Guid.Parse("00000000-0000-0000-0000-000000000005"), "Islamic studies", "K. Fahad", "101", (int)DayOfWeek.Wednesday, 2),
            // The same teacher also teaches another class on Monday, period 2: moving Mathematics there is a teacher clash.
            new(Guid.Parse("00000000-0000-0000-0000-000000000006"), "Mathematics (class 5B)", "A. Saleh", "102", (int)DayOfWeek.Monday, 2, InThisClass: false),
        ];
    }

    /// <summary>Block refuses a clashing move; Warn saves it and reports the clashes.</summary>
    public bool BlockOnConflict { get; set; }

    /// <summary>Working days in the order of the week (Sunday to Thursday, the default <c>Timetable.WorkingDays</c>).</summary>
    public IReadOnlyList<DayOfWeek> Days { get; } = [DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday];

    public int Periods { get; } = 7;

    public IReadOnlyList<SpikeSlot> Slots => _slots;

    public SpikeSlot? At(int day, int period) => _slots.FirstOrDefault(s => s.InThisClass && s.Day == day && s.Period == period);

    public SpikeSlot? Find(Guid id) => _slots.FirstOrDefault(s => s.Id == id);

    public static string StartOf(int period) => TimeOnly.FromTimeSpan(new TimeSpan(7, 30, 0) + TimeSpan.FromMinutes(50 * (period - 1))).ToString("HH:mm");

    public static string EndOf(int period) => TimeOnly.FromTimeSpan(new TimeSpan(7, 30, 0) + TimeSpan.FromMinutes((50 * (period - 1)) + 45)).ToString("HH:mm");

    /// <summary>Moves a slot of this class to another cell and answers like the server would.</summary>
    public SpikeMoveResult Move(Guid id, int day, int period)
    {
        var slot = Find(id) ?? throw new ArgumentException("unknown slot", nameof(id));
        if (slot.Day == day && slot.Period == period) return new SpikeMoveResult(true, []);

        var conflicts = new List<(SpikeConflict, SpikeSlot)>();
        if (At(day, period) is { } same && same.Id != id) conflicts.Add((SpikeConflict.ClassDoubleBooked, same));
        foreach (var other in _slots.Where(s => s.Id != id && s.Day == day && s.Period == period && s.Teacher == slot.Teacher && !conflicts.Any(c => c.Item2.Id == s.Id)))
        {
            conflicts.Add((SpikeConflict.TeacherDoubleBooked, other));
        }

        if (conflicts.Count > 0 && BlockOnConflict) return new SpikeMoveResult(false, conflicts);

        _slots[_slots.IndexOf(slot)] = slot with { Day = day, Period = period };
        return new SpikeMoveResult(true, conflicts);
    }

    /// <summary>The slots of this class that currently clash with another lesson (shown as a warning chip in Warn mode).</summary>
    public IReadOnlySet<Guid> Warnings()
    {
        var result = new HashSet<Guid>();
        foreach (var slot in _slots.Where(s => s.InThisClass))
        {
            if (_slots.Any(o => o.Id != slot.Id && o.Day == slot.Day && o.Period == slot.Period && (o.InThisClass || o.Teacher == slot.Teacher)))
            {
                result.Add(slot.Id);
            }
        }

        return result;
    }
}
