namespace Lms.Domain.LiveSessions;

/// <summary>
/// Values of the live-session contract the domain needs (the domain does not reference <c>Lms.Shared</c>; a unit test pins
/// them to <c>Lms.Shared.LiveSessions</c>).
/// </summary>
public static class LiveSessionRules
{
    public const int MaxOccurrences = 52;
    public const int MinIntervalWeeks = 1;

    public const string Scheduled = "Scheduled";
    public const string Live = "Live";
    public const string Ended = "Ended";
    public const string Cancelled = "Cancelled";
    public const string SyncFailed = "SyncFailed";

    public const string RecordingProvider = "Provider";
    public const string RecordingManual = "Manual";

    public const string Matched = "Matched";
    public const string Guest = "Guest";
}
