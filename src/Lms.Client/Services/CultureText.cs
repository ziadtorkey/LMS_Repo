using System.Globalization;

namespace Lms.Client.Services;

/// <summary>Picks the Arabic or English variant of a stored business name (NameAr / NameEn).</summary>
public static class CultureText
{
    public static string Pick(string ar, string en) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? ar : en;
}
