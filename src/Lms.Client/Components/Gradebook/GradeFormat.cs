using System.Globalization;

namespace Lms.Client.Components.Gradebook;

/// <summary>Display formatting only (L18: the client never calculates a grade). Numbers are left to right and use the gradebook's decimal places.</summary>
public static class GradeFormat
{
    public static string Points(decimal? value) => value is { } v ? v.ToString("0.##", CultureInfo.InvariantCulture) : "–";

    /// <summary>"Withdrew on 2027-01-12" (the date left to right); "Withdrawn" when the day is unknown. Text, not only a colour.</summary>
    public static string Withdrawn(Microsoft.Extensions.Localization.IStringLocalizer<Lms.Client.Resources.SharedResource> l, CultureInfo culture, DateOnly? on) =>
        on is { } d ? string.Format(culture, l["Grd_Withdrew_On"].Value, d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : l["Grd_Withdrawn"].Value;

    public static string Percent(decimal? value, int places) =>
        value is { } v ? v.ToString("F" + Math.Clamp(places, 0, 4), CultureInfo.InvariantCulture) : "–";
}
