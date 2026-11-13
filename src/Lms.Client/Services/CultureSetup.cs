using System.Globalization;

namespace Lms.Client.Services;

public static class CultureSetup
{
    /// <summary>
    /// The culture the app runs in: the requested UI culture (ar or en, so texts, month names and RTL follow it) with
    /// Gregorian dates and invariant number formatting. The Arabic number format writes a negative number with
    /// U+061C (ARABIC LETTER MARK) before the minus sign, which breaks attributes such as <c>tabindex="-1"</c> (#508);
    /// the app shows Western digits in both languages anyway.
    /// </summary>
    public static CultureInfo ForLms(CultureInfo culture)
    {
        var clone = (CultureInfo)culture.Clone();
        if (clone.DateTimeFormat.Calendar is not GregorianCalendar) clone.DateTimeFormat.Calendar = new GregorianCalendar();
        clone.NumberFormat = (NumberFormatInfo)NumberFormatInfo.InvariantInfo.Clone();
        return clone;
    }

    /// <summary>Arabic UI uses Gregorian dates (some Arabic cultures default to Umm al-Qura/Hijri).</summary>
    public static CultureInfo WithGregorianCalendar(CultureInfo culture)
    {
        if (culture.DateTimeFormat.Calendar is GregorianCalendar) return culture;
        var clone = (CultureInfo)culture.Clone();
        clone.DateTimeFormat.Calendar = new GregorianCalendar();
        return clone;
    }

    /// <summary>Applies <see cref="ForLms"/> to the culture the app was started in (index.html sets it from lms.culture).</summary>
    public static void Apply()
    {
        CultureInfo.DefaultThreadCurrentCulture = ForLms(CultureInfo.CurrentCulture);
        CultureInfo.DefaultThreadCurrentUICulture = ForLms(CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture;
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture;
    }
}
