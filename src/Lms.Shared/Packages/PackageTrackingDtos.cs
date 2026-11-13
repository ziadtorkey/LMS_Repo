using System.Text.RegularExpressions;

namespace Lms.Shared.Packages;

/// <summary>Size limits of tracking messages (the same on the page and, authoritatively, on the server).</summary>
public static class PackageTrackingLimits
{
    /// <summary>One commit message, serialised.</summary>
    public const int MaxMessageBytes = 8 * 1024;

    /// <summary>One value of the data model.</summary>
    public const int MaxValueBytes = 4 * 1024;

    /// <summary>The whole data model kept for one attempt.</summary>
    public const int MaxCmiBytes = 64 * 1024;
}

/// <summary>
/// The data-model keys a package may write (the allow-list the server enforces; everything else is dropped). Indexed
/// collections use <c>{n}</c> for the index. Read-only elements (student name, mastery score, launch data) are never written
/// by content and are not listed. Draft: the ADR (M4b Task 3) confirms it against the real packages.
/// </summary>
public static partial class ScormCmiKeys
{
    public static IReadOnlyList<string> Scorm12 { get; } =
    [
        "cmi.core.lesson_location", "cmi.core.lesson_status", "cmi.core.exit", "cmi.core.session_time",
        "cmi.core.score.raw", "cmi.core.score.min", "cmi.core.score.max", "cmi.suspend_data", "cmi.comments",
        "cmi.student_preference.audio", "cmi.student_preference.language", "cmi.student_preference.speed", "cmi.student_preference.text",
        "cmi.objectives.{n}.id", "cmi.objectives.{n}.score.raw", "cmi.objectives.{n}.score.min", "cmi.objectives.{n}.score.max",
        "cmi.objectives.{n}.status",
        "cmi.interactions.{n}.id", "cmi.interactions.{n}.time", "cmi.interactions.{n}.type", "cmi.interactions.{n}.weighting",
        "cmi.interactions.{n}.student_response", "cmi.interactions.{n}.result", "cmi.interactions.{n}.latency",
    ];

    public static IReadOnlyList<string> Scorm2004 { get; } =
    [
        "cmi.completion_status", "cmi.success_status", "cmi.location", "cmi.suspend_data", "cmi.exit", "cmi.session_time",
        "cmi.progress_measure", "cmi.score.scaled", "cmi.score.raw", "cmi.score.min", "cmi.score.max",
        "cmi.learner_preference.audio_level", "cmi.learner_preference.language", "cmi.learner_preference.delivery_speed",
        "cmi.learner_preference.audio_captioning",
        "cmi.objectives.{n}.id", "cmi.objectives.{n}.score.scaled", "cmi.objectives.{n}.score.raw", "cmi.objectives.{n}.score.min",
        "cmi.objectives.{n}.score.max", "cmi.objectives.{n}.success_status", "cmi.objectives.{n}.completion_status",
        "cmi.objectives.{n}.progress_measure", "cmi.objectives.{n}.description",
        "cmi.interactions.{n}.id", "cmi.interactions.{n}.type", "cmi.interactions.{n}.timestamp", "cmi.interactions.{n}.weighting",
        "cmi.interactions.{n}.learner_response", "cmi.interactions.{n}.result", "cmi.interactions.{n}.latency",
        "cmi.interactions.{n}.description",
    ];

    /// <summary>The flattened xAPI result elements an H5P content may report.</summary>
    public static IReadOnlyList<string> Xapi { get; } =
    [
        "verb", "result.completion", "result.success", "result.score.scaled", "result.score.raw", "result.score.min",
        "result.score.max", "result.duration",
    ];

    /// <summary>True when <paramref name="key"/> (an index written as digits, e.g. <c>cmi.objectives.0.id</c>) is on the list of the format and SCORM version.</summary>
    public static bool IsAllowed(string format, string? scormVersion, string key)
    {
        if (string.Equals(format, PackageCommitFormats.Xapi, StringComparison.Ordinal))
        {
            return Xapi.Contains(key, StringComparer.Ordinal);
        }

        var list = string.Equals(scormVersion, ScormVersions.Scorm2004, StringComparison.Ordinal) ? Scorm2004 : Scorm12;
        return list.Contains(Index().Replace(key, ".{n}."), StringComparer.Ordinal);
    }

    [GeneratedRegex(@"\.\d{1,3}\.")]
    private static partial Regex Index();
}
