using System.Text;
using System.Text.Json;
using Lms.Domain.Packages;
using Lms.Shared.Packages;

namespace Lms.Infrastructure.Packages.Attempts;

/// <summary>Why a commit was refused before it touched the attempt.</summary>
public enum CommitRefusal
{
    None,

    /// <summary>The format does not match the package kind, a key is not on the allow-list, or a size cap is exceeded (400).</summary>
    Invalid,
}

/// <summary>
/// What the server enforces on a commit that the page only guards (ADR 0005 section 4): the format of the package kind, the key
/// allow-list, the message (8 KB), value (4 KB) and data model (64 KB) caps. A value outside the allow-list is refused, not dropped:
/// a content that writes an unknown key is not a SCORM content the platform can trust.
/// </summary>
public static class CommitGate
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The <c>PackageCommitFormats</c> value a package kind reports in.</summary>
    public static string FormatOf(string kind) => kind == PackageKinds.H5p ? PackageCommitFormats.Xapi : PackageCommitFormats.Scorm;

    public static CommitRefusal Check(PackageUpload package, PackageCommitRequest request)
    {
        if (request.Data is null || request.Format != FormatOf(package.Kind) || request.Seq < 0)
        {
            return CommitRefusal.Invalid;
        }

        if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(request, Json)) > PackageTrackingLimits.MaxMessageBytes)
        {
            return CommitRefusal.Invalid;
        }

        foreach (var (key, value) in request.Data)
        {
            if (value is null || Encoding.UTF8.GetByteCount(value) > PackageTrackingLimits.MaxValueBytes
                || !ScormCmiKeys.IsAllowed(request.Format, package.Version, key))
            {
                return CommitRefusal.Invalid;
            }
        }

        return CommitRefusal.None;
    }

    /// <summary>The merged data model, or null when it would pass the per-attempt cap.</summary>
    public static string? Merge(IReadOnlyDictionary<string, string> current, IReadOnlyDictionary<string, string> incoming)
    {
        var merged = new Dictionary<string, string>(current);
        foreach (var (key, value) in incoming)
        {
            merged[key] = value;
        }

        var json = PackageAttempt.Serialize(merged);
        return Encoding.UTF8.GetByteCount(json) > PackageTrackingLimits.MaxCmiBytes ? null : json;
    }
}
