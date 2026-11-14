namespace Lms.Shared.Attendance;

// M5 contract, wave 3 (M5 plan Task 3): attendance sessions and records, the live-session proposal and absence alerts. D58, D61, L11, L14, L15, L25.

/// <summary>The status of one student in one attendance session. An Open session may have unmarked students (no status yet); a Taken one may not.</summary>
public static class AttendanceStatuses
{
    public const string Present = "Present";
    public const string Absent = "Absent";
    public const string Late = "Late";

    /// <summary>Set by an approved excuse (one transaction with an <c>AttendanceRecordChange</c>) or by hand; it never counts as an absence in alerts.</summary>
    public const string Excused = "Excused";

    public static IReadOnlyList<string> All { get; } = [Present, Absent, Late, Excused];
}

/// <summary>Where an attendance session comes from.</summary>
public static class AttendanceSources
{
    /// <summary>Generated from a published timetable slot (the generator runs per client, in its own context).</summary>
    public const string Timetable = "Timetable";

    /// <summary>Created by a teacher for a ClassCourse with no slot.</summary>
    public const string AdHoc = "AdHoc";

    /// <summary>Proposed from the participants of an ended live session; nothing is final until the teacher confirms (D58, L11).</summary>
    public const string Live = "Live";

    public static IReadOnlyList<string> All { get; } = [Timetable, AdHoc, Live];
}

/// <summary>Whether attendance was taken. A <c>Live</c> session is <c>Open</c> until the teacher confirms.</summary>
public static class AttendanceSessionStates
{
    public const string Open = "Open";
    public const string Taken = "Taken";

    public static IReadOnlyList<string> All { get; } = [Open, Taken];
}

/// <summary>What an <c>AttendanceRecordChange</c> row records.</summary>
public static class AttendanceChangeKinds
{
    /// <summary>The first time the session was taken or confirmed.</summary>
    public const string Taken = "Taken";

    /// <summary>An edit inside the edit window.</summary>
    public const string Edited = "Edited";

    /// <summary>An edit after the window by a user with <c>Attendance.Edit</c>; the reason is required (L14).</summary>
    public const string EditedAfterWindow = "EditedAfterWindow";

    /// <summary>An approved excuse turned the record into <c>Excused</c>.</summary>
    public const string ExcuseApproved = "ExcuseApproved";

    public static IReadOnlyList<string> All { get; } = [Taken, Edited, EditedAfterWindow, ExcuseApproved];
}

/// <summary>The kinds of the attendance reports (<c>GET /api/attendance-reports/{kind}</c>).</summary>
public static class AttendanceReportKinds
{
    public const string Student = "student";
    public const string Class = "class";

    public static IReadOnlyList<string> All { get; } = [Student, Class];
}

/// <summary>Limits of the attendance contract. Local days always come from <c>IClientCalendar</c> (L2); the edit window is <c>Attendance.EditWindowHours</c>.</summary>
public static class AttendanceLimits
{
    /// <summary>Length of a per-student note.</summary>
    public const int NoteMaxLength = 200;

    /// <summary>Length of the reason of an edit after the window.</summary>
    public const int ReasonMinLength = 1;

    public const int ReasonMaxLength = 500;

    /// <summary>The take page shows up to this many roster rows, with a search box and no virtualisation (L17).</summary>
    public const int RosterMaxRows = 200;

    /// <summary>The widest date range of the unit-level session search.</summary>
    public const int SearchMaxDays = 92;

    public const int EditWindowHoursMin = 1;
    public const int EditWindowHoursMax = 720;
    public const int EditWindowHoursDefault = 48;

    public const int GenerateDaysAheadMin = 1;
    public const int GenerateDaysAheadMax = 30;
    public const int GenerateDaysAheadDefault = 7;

    /// <summary>A live participant counts as present when the merged presence is at least this percent of the scheduled length (L11 item 6).</summary>
    public const int LiveMinPercentDefault = 50;

    /// <summary>A live participant who first joined this many minutes after the start is proposed <c>Late</c>.</summary>
    public const int LateAfterMinutesDefault = 10;

    public const int AbsentThresholdMin = 1;
    public const int AbsentThresholdMax = 100;
    public const int AbsentThresholdDefault = 5;

    public const int ThresholdWindowDaysMin = 7;
    public const int ThresholdWindowDaysMax = 365;
    public const int ThresholdWindowDaysDefault = 30;

    /// <summary>A <c>Late</c> counts as this percent of an absence in the alert score (D61: half).</summary>
    public const int LateWeightPercentMin = 0;
    public const int LateWeightPercentMax = 100;
    public const int LateWeightPercentDefault = 50;
}
