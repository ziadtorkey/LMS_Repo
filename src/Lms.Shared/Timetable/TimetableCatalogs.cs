namespace Lms.Shared.Timetable;

// M5 contract, wave 2 (M5 plan Task 2): periods, working days, timetable slots, conflicts, publishing, exceptions and substitutes. D60, D64.

/// <summary>Whether a slot is visible beyond the timetable builders (FR-TT-02).</summary>
public static class SlotStates
{
    /// <summary>Being built: only staff with <c>Timetable.Manage</c> see it; it generates no attendance sessions.</summary>
    public const string Draft = "Draft";

    /// <summary>Visible in every view and the source of attendance sessions (FR-TT-06).</summary>
    public const string Published = "Published";

    public static IReadOnlyList<string> All { get; } = [Draft, Published];
}

/// <summary>What a save does with a conflicting slot (setting <c>Timetable.ConflictMode</c>, FR-TT-03).</summary>
public static class ConflictModes
{
    /// <summary>The save is refused with 409 <c>timetable_conflict</c> and the clashing slots.</summary>
    public const string Block = "Block";

    /// <summary>The save succeeds and the clashes come back as warnings.</summary>
    public const string Warn = "Warn";

    public static IReadOnlyList<string> All { get; } = [Block, Warn];
}

/// <summary>The clashes the server detects. Rooms are free text (D60), so there is no room clash.</summary>
public static class ConflictKinds
{
    /// <summary>The same teacher on the same day and period in another slot of the term.</summary>
    public const string TeacherDoubleBooked = "TeacherDoubleBooked";

    /// <summary>The same class on the same day and period in another slot (any ClassCourse) of the term.</summary>
    public const string ClassDoubleBooked = "ClassDoubleBooked";

    public static IReadOnlyList<string> All { get; } = [TeacherDoubleBooked, ClassDoubleBooked];
}

/// <summary>A dated change to one slot that never edits the base timetable (FR-TT-05).</summary>
public static class ExceptionKinds
{
    /// <summary>Another teacher takes this one date; that teacher becomes the substitute for that session only (L15).</summary>
    public const string Substitute = "Substitute";

    /// <summary>No session on that date.</summary>
    public const string Cancelled = "Cancelled";

    /// <summary>The session of that date happens in another period of the same day.</summary>
    public const string Moved = "Moved";

    public static IReadOnlyList<string> All { get; } = [Substitute, Cancelled, Moved];
}

/// <summary>Markers a slot card can carry in the builder and the views.</summary>
public static class SlotBadges
{
    /// <summary>The slot clashes with another one (only in <c>Warn</c> mode can a clashing slot exist).</summary>
    public const string Conflict = "Conflict";

    /// <summary>At least one exception is dated inside the shown range.</summary>
    public const string HasException = "HasException";

    /// <summary>The slot changed after it was published and is not yet re-published.</summary>
    public const string DraftChange = "DraftChange";

    public static IReadOnlyList<string> All { get; } = [Conflict, HasException, DraftChange];
}

/// <summary>Why a slot was not copied from the source term (<c>CopyTermResultDto</c>).</summary>
public static class CopySkipReasons
{
    public const string ClassCourseMissing = "ClassCourseMissing";
    public const string TeacherMissing = "TeacherMissing";
    public const string PeriodMissing = "PeriodMissing";

    /// <summary>The target term already has a slot at that day and period for the class.</summary>
    public const string SlotExists = "SlotExists";

    public static IReadOnlyList<string> All { get; } = [ClassCourseMissing, TeacherMissing, PeriodMissing, SlotExists];
}

/// <summary>The weekday names the <c>Timetable.WorkingDays</c> setting uses (English short names, never localised in the value).</summary>
public static class WorkingDayNames
{
    public const string Sunday = "Sun";
    public const string Monday = "Mon";
    public const string Tuesday = "Tue";
    public const string Wednesday = "Wed";
    public const string Thursday = "Thu";
    public const string Friday = "Fri";
    public const string Saturday = "Sat";

    public static IReadOnlyList<string> All { get; } = [Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday];

    /// <summary>The default working week: Sunday to Thursday (D58).</summary>
    public static IReadOnlyList<string> Default { get; } = [Sunday, Monday, Tuesday, Wednesday, Thursday];
}

/// <summary>Validation limits of the timetable contract.</summary>
public static class TimetableLimits
{
    public const int MaxRoomLength = 100;
    public const int MaxNoteLength = 500;
    public const int MaxNameLength = 100;
    public const int MaxPeriodsPerUnit = 20;
}
