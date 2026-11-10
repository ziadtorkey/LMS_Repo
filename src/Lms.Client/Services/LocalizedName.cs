namespace Lms.Client.Services;

public static class LocalizedName
{
    /// <summary>Picks the Arabic or English variant of a business name for the current UI culture.</summary>
    public static string Pick(this ICultureService culture, string ar, string en) =>
        culture.Current.TwoLetterISOLanguageName == "ar" ? ar : en;
}
