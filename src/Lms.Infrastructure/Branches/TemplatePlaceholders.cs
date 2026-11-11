using System.Text.RegularExpressions;

namespace Lms.Infrastructure.Branches;

/// <summary>Placeholder tokens like <c>{Year}</c> in template names and codes.</summary>
public static partial class TemplatePlaceholders
{
    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9_]*)\}")]
    private static partial Regex Token();

    [GeneratedRegex(@"^\{[A-Za-z][A-Za-z0-9_]*\}$")]
    private static partial Regex WholeToken();

    /// <summary>True when the text is exactly one token, e.g. <c>{Year}</c>.</summary>
    public static bool IsToken(string text) => WholeToken().IsMatch(text);

    /// <summary>Replaces each literal key of <paramref name="map"/> by its placeholder (longest keys first).</summary>
    public static string Parameterize(string text, IReadOnlyDictionary<string, string> map)
    {
        foreach (var (literal, placeholder) in map.Where(m => m.Key.Length > 0).OrderByDescending(m => m.Key.Length))
        {
            text = text.Replace(literal, placeholder, StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>The distinct placeholder names (without braces) used in the text.</summary>
    public static IReadOnlySet<string> Find(string text) =>
        Token().Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>Replaces <c>{Name}</c> by <c>values[Name]</c>; tokens without a value stay as they are.</summary>
    public static string Apply(string text, IReadOnlyDictionary<string, string> values) =>
        Token().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
}
