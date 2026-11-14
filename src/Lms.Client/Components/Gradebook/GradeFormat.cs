using System.Globalization;

namespace Lms.Client.Components.Gradebook;

/// <summary>Display formatting only (L18: the client never calculates a grade). Numbers are left to right and use the gradebook's decimal places.</summary>
public static class GradeFormat
{
    public static string Points(decimal? value) => value is { } v ? v.ToString("0.##", CultureInfo.InvariantCulture) : "–";

    public static string Percent(decimal? value, int places) =>
        value is { } v ? v.ToString("F" + Math.Clamp(places, 0, 4), CultureInfo.InvariantCulture) : "–";
}
