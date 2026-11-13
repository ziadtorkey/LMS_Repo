using System.Text.Json;
using Lms.Shared.Packages;

namespace Lms.Infrastructure.Packages.Scorm;

/// <summary>
/// The per-request data block of a package page (M4b Task 10, Decision 11): the attempt's current data model, so the prelude answers
/// <c>Initialize</c> / <c>GetValue</c> at once with no port. Only keys the content may write (<see cref="ScormCmiKeys"/>) are carried, which
/// is also why the student's name, id and any other read-only or personal element can never reach the frame; the entry mode and the data
/// version travel as separate fields.
/// </summary>
public static class ScormInitialDataBlock
{
    /// <summary>The JSON of the block. <paramref name="entry"/> is <c>ab-initio</c> for a first launch and <c>resume</c> for a returning one.</summary>
    public static string Build(string scormVersion, IReadOnlyDictionary<string, string> model, string entry, long lastSeq)
    {
        var kept = model.Where(p => ScormCmiKeys.IsAllowed(PackageCommitFormats.Scorm, scormVersion, p.Key))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        return JsonSerializer.Serialize(new { version = scormVersion, entry, seq = lastSeq, model = kept });
    }
}
