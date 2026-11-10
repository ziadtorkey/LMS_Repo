using System.Globalization;

namespace Lms.Client.Services;

public static class CultureSetup
{
    /// <summary>Arabic UI uses Gregorian dates (some Arabic cultures default to Umm al-Qura/Hijri).</summary>
    public static CultureInfo WithGregorianCalendar(CultureInfo culture)
    {
        if (culture.DateTimeFormat.Calendar is GregorianCalendar) return culture;
        var clone = (CultureInfo)culture.Clone();
        clone.DateTimeFormat.Calendar = new GregorianCalendar();
        return clone;
    }

    public static void ApplyGregorian()
    {
        CultureInfo.DefaultThreadCurrentCulture = WithGregorianCalendar(CultureInfo.CurrentCulture);
        CultureInfo.DefaultThreadCurrentUICulture = WithGregorianCalendar(CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture;
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture;
    }
}
