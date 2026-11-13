namespace Lms.Shared.LiveSessions;

/// <summary>
/// Lifecycle of one live session occurrence (FR-VC-01). <c>Scheduled</c> = the Zoom meeting exists; <c>Live</c> and
/// <c>Ended</c> come from Zoom events (webhook or polling); <c>Cancelled</c> = deleted by the teacher (the row stays,
/// soft delete); <c>SyncFailed</c> = the provider call failed, the row is kept and the sweep retries (no manual retry
/// in v1, Q-G2).
/// </summary>
public static class LiveSessionStatuses
{
    public const string Scheduled = "Scheduled";
    public const string Live = "Live";
    public const string Ended = "Ended";
    public const string Cancelled = "Cancelled";
    public const string SyncFailed = "SyncFailed";

    public static IReadOnlyList<string> All { get; } = [Scheduled, Live, Ended, Cancelled, SyncFailed];
}

/// <summary>Where a recording link came from: Zoom cloud recording (only when enabled for the unit, D55) or pasted by the teacher.</summary>
public static class RecordingSources
{
    public const string Provider = "Provider";
    public const string Manual = "Manual";

    public static IReadOnlyList<string> All { get; } = [Provider, Manual];
}

/// <summary>
/// How a Zoom participant was matched (Decision 7, Z5): <c>Matched</c> = Zoom reports the participant as authenticated
/// and the verified email equals the account email of a student enrolled in the session's ClassCourse; everything
/// else, including a guest who types a student's email, is <c>Guest</c> and is never attached to a student.
/// </summary>
public static class ParticipantMatchStates
{
    public const string Matched = "Matched";
    public const string Guest = "Guest";

    public static IReadOnlyList<string> All { get; } = [Matched, Guest];
}

/// <summary>Which occurrences of a series an update or delete touches (Q-G3).</summary>
public static class SeriesEditScopes
{
    /// <summary>Only this occurrence (the default; the only scope for a single session).</summary>
    public const string This = "This";

    /// <summary>This occurrence and every later one of the series; earlier occurrences are never changed.</summary>
    public const string ThisAndFollowing = "ThisAndFollowing";

    public static IReadOnlyList<string> All { get; } = [This, ThisAndFollowing];
}

/// <summary>Weekdays of a weekly recurrence, spelled as <see cref="DayOfWeek"/> names so the server can parse them.</summary>
public static class RecurrenceDays
{
    public static IReadOnlyList<string> All { get; } =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
}

/// <summary>Validation limits of the live-session contract (M4b plan Task 1, Decisions 2, 6 and 9).</summary>
public static class LiveSessionLimits
{
    /// <summary>Length of <c>TitleAr</c> / <c>TitleEn</c> (also the Zoom topic: the title only, no markup, Decision 5).</summary>
    public const int TitleMinLength = 1;

    public const int TitleMaxLength = 200;

    public const int DurationMinMinutes = 15;

    public const int DurationMaxMinutes = 480;

    /// <summary>Most occurrences a series may have (one Zoom meeting each).</summary>
    public const int MaxOccurrences = 52;

    /// <summary>Smallest week interval of a weekly recurrence.</summary>
    public const int MinIntervalWeeks = 1;

    /// <summary>Most rows GET /api/live-sessions/upcoming returns.</summary>
    public const int UpcomingMax = 5;

    /// <summary>Longest recording link accepted (http/https only).</summary>
    public const int RecordingUrlMaxLength = 2048;

    /// <summary>
    /// Server configuration key of the per-client host share (Decision 2): the share of the platform host pool one
    /// client may hold in any overlapping slot (1–100 %, at least one host). Platform-level, so it is read from server
    /// configuration and is deliberately not a unit setting in <c>SettingCatalog</c>: no client admin can change it.
    /// </summary>
    public const string MaxHostSharePercentConfigKey = "Live:MaxHostSharePercent";

    public const int MaxHostSharePercentDefault = 25;

    public const int MaxHostSharePercentMin = 1;

    public const int MaxHostSharePercentMax = 100;
}
