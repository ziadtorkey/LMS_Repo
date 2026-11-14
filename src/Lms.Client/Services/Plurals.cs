using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Services;

/// <summary>
/// Counted text (design spec M4b §1 and §8): a number next to a noun is never built by a page. Each such text has four resx keys
/// with the suffixes <c>_One</c> (1), <c>_Two</c> (2), <c>_Few</c> (3–10) and <c>_Many</c> (11 and more, and 0). English
/// uses <c>_One</c> for the singular and puts the same plural in the other three.
/// </summary>
public static class Plurals
{
    public static string Suffix(int n, bool arabic)
    {
        if (!arabic) return n == 1 ? "One" : "Many";
        return n switch
        {
            1 => "One",
            2 => "Two",
            >= 3 and <= 10 => "Few",
            _ => "Many",
        };
    }

    /// <summary>The text of <paramref name="baseKey"/> for <paramref name="n"/> ("Live_Unit_Hour" → "Live_Unit_Hour_Few"), with <c>{0}</c> = the number.</summary>
    public static string Count(this IStringLocalizer localizer, string baseKey, int n, bool arabic) =>
        localizer[$"{baseKey}_{Suffix(n, arabic)}", n.ToString(CultureInfo.InvariantCulture)].Value;
}
