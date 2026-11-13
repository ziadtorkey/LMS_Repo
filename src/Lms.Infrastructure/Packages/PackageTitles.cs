namespace Lms.Infrastructure.Packages;

/// <summary>The title a package shows to teachers and students: hidden, control and bidirectional-override characters removed, trimmed, at most the column length.</summary>
public static class PackageTitles
{
    public const int MaxLength = 500;

    /// <summary>The cleaned title, or null when nothing is left.</summary>
    public static string? Clean(string? title)
    {
        if (title is null)
        {
            return null;
        }

        var cleaned = new string(title.Where(c => !ZipNameRules.IsHidden(c)).ToArray()).Trim();
        if (cleaned.Length > MaxLength)
        {
            cleaned = cleaned[..MaxLength];
            // Never cut a surrogate pair in half.
            if (char.IsHighSurrogate(cleaned[^1]))
            {
                cleaned = cleaned[..^1];
            }
        }

        return cleaned.Length == 0 ? null : cleaned;
    }
}
