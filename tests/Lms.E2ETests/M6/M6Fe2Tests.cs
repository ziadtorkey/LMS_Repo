using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M6;

/// <summary>
/// DRAFT (QA3, stacked on #756): M6 FE2 pages. Skipped until the M6 backend (Tasks 6 to 12) is on main so the pages have data. Scenarios: the QA3 plan in the shared notes
/// (qa3-m6-fe2-e2e-plan.md). Remove the Skip as each page's API lands, take the e2e lock before any run.
/// </summary>
public sealed class M6Fe2Tests(E2EFixture fixture) : E2ETestBase(fixture)
{
    private const string Pending = "M6 backend not on main yet (draft specs, see qa3-m6-fe2-e2e-plan.md)";

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Messaging_rules_change_is_kept_after_save_and_reverted_by_discard(string culture)
    {
        await StartAsync(culture: culture);
        await SignInAsSeededAdminAsync();
        var rules = new MessagingRulesPage(Page);
        await rules.OpenAsync();

        var cell = rules.Cell("Teacher", "Student");
        var before = await cell.IsCheckedAsync();
        await cell.SetCheckedAsync(!before);
        await Expect(rules.Bar).ToBeVisibleAsync();
        await rules.DiscardAsync();
        Assert.Equal(before, await cell.IsCheckedAsync());

        await cell.SetCheckedAsync(!before);
        await rules.SaveAsync();
        await Page.ReloadAsync();
        await rules.Title.WaitForAsync();
        Assert.Equal(!before, await cell.IsCheckedAsync());
        await AssertDirectionAsync(culture);
    }

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Communication_settings_invalid_limit_is_refused_and_valid_one_persists(string culture)
    {
        await StartAsync(culture: culture);
        await SignInAsSeededAdminAsync();
        var comms = new CommunicationSettingsPage(Page);
        await comms.OpenAsync();

        await comms.Field("EditWindowMinutes").FillAsync("-1");
        await comms.SaveAsync();
        await Expect(comms.Error).ToBeVisibleAsync();

        await comms.Field("EditWindowMinutes").FillAsync("20");
        await comms.SaveAsync();
        await Page.ReloadAsync();
        await comms.Title.WaitForAsync();
        await Expect(comms.Field("EditWindowMinutes")).ToHaveValueAsync("20");
    }

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Preferences_toggle_is_saved_and_discard_reverts(string culture)
    {
        await StartAsync(culture: culture);
        await SignInAsSeededAdminAsync();
        var prefs = new NotificationPreferencesPage(Page);
        await prefs.OpenAsync();

        var cell = prefs.Cell("announcement.published", "Email");
        var before = await cell.IsCheckedAsync();
        await cell.SetCheckedAsync(!before);
        await Expect(prefs.Bar).ToBeVisibleAsync();
        await prefs.DiscardAsync();
        Assert.Equal(before, await cell.IsCheckedAsync());
    }

    private async Task SignInAsSeededAdminAsync()
    {
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();
        await Expect(shell.ContextSwitcher).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
    }

    private async Task AssertDirectionAsync(string culture) =>
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
}
