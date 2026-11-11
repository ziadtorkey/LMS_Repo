using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using Microsoft.Playwright;

namespace Lms.E2ETests.M1;

/// <summary>Shared steps for the M1 scenarios: sign-in in a language, seeded names, and unique names per run.</summary>
internal static class M1Kit
{
    /// <summary>The seeded school (client root); its sole Org Admin is orgadmin@ (dev seed).</summary>
    public static string SchoolName(string culture) => culture == "ar" ? "مدرسة التجمع" : "School Altagamo3";

    public static string StageLevel(string culture) => culture == "ar" ? "مرحلة" : "Stage";

    public static string SectionLevel(string culture) => culture == "ar" ? "فصل" : "Section";

    /// <summary>The seeded built-in role label (business data, not a UI resource).</summary>
    public static string SupervisorRole(string culture) => culture == "ar" ? "المشرف الأكاديمي" : "Supervisor";

    public static string Text(string culture, string key) => culture == "ar" ? Resx.Ar(key) : Resx.En(key);

    public static string Pick(string culture, string ar, string en) => culture == "ar" ? ar : en;

    /// <summary>A short id that keeps names and codes unique when the suite runs again on the same database.</summary>
    public static string RunId() => DateTime.UtcNow.ToString("HHmmss") + Guid.NewGuid().ToString("N")[..4];

    /// <summary>Signs in, picks a context when the user has several, and switches the UI to <paramref name="culture"/>.</summary>
    public static async Task SignInAsync(IPage page, string email, string password, string culture, params string[] contextFragments)
    {
        await new LoginPage(page).SignInAsync(email, password);
        if (page.Url.Contains("/choose-context", StringComparison.Ordinal))
        {
            await new ChooseContextPage(page).ChooseAsync(contextFragments);
        }

        var shell = new ShellPage(page);
        await shell.WaitReadyAsync();
        if (culture == "en" && await shell.HtmlLangAsync() != "en")
        {
            await shell.SwitchLanguageAsync();
        }
    }
}
