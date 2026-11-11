using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M1;

/// <summary>
/// M1 exit demo 1 (plan Task 36, #71): clients with first admins, the invitation email, level types and trees, in
/// Arabic (RTL) and English. Each test creates its own client with unique names (<see cref="M1Names"/>).
/// </summary>
public sealed class ClientsAndTreesTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    // Verified green locally against main + #92 + #83 (6/6, ar and en). Remove each skip when its PR is on main.
    private const string NeedsClientsApi = "Needs #92 (clients API, closes #42) on main.";
    private const string NeedsClientsAndLevelTypesApi = "Needs #92 (clients API) and #83 (level types API) on main.";

    [Theory(Skip = NeedsClientsApi)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Platform_admin_creates_school_group_and_university_with_first_admins(string culture)
    {
        var names = M1Names.New();
        await StartAsync(culture: culture);
        await SignInAsPlatformAdminAsync();
        await AssertDirectionAsync(culture);

        var clients = new ClientsAdminPage(Page, culture);
        await clients.OpenAsync();
        await clients.CreateAsync(SchoolGroup(names));
        await clients.CreateAsync(University(names));

        var schools = await clients.FindRowAsync(culture == "ar" ? names.SchoolGroupAr : names.SchoolGroupEn);
        await Expect(schools).ToContainTextAsync(Resx.In(culture, "ClientType_SchoolGroup"));
        await Expect(schools).ToContainTextAsync(Resx.In(culture, "ClientStatus_Active"));

        var university = await clients.FindRowAsync(culture == "ar" ? names.UniversityAr : names.UniversityEn);
        await Expect(university).ToContainTextAsync(Resx.In(culture, "ClientType_University"));

        // Both first admins were invited (new users).
        using var mail = new MailpitClient();
        Assert.Contains("/Account/ResetPassword", await mail.WaitForAccountLinkAsync(names.AdminEmail("school")));
        Assert.Contains("/Account/ResetPassword", await mail.WaitForAccountLinkAsync(names.AdminEmail("uni")));
    }

    [Theory(Skip = NeedsClientsApi)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Org_admin_sets_password_from_invitation_email_and_logs_in(string culture)
    {
        var names = M1Names.New();
        await StartAsync(culture: culture);
        await SignInAsPlatformAdminAsync();
        var clients = new ClientsAdminPage(Page, culture);
        await clients.OpenAsync();
        await clients.CreateAsync(SchoolGroup(names));
        await SignOutAsync();

        await InvitedAdminSignsInAsync(names.AdminEmail("school"), names.NewPassword);

        // The new admin's only assignment is selected automatically and the client-wide menu is there.
        var shell = new ShellPage(Page);
        await Expect(shell.Nav("orgunits")).ToBeVisibleAsync();
        await Expect(shell.Nav("leveltypes")).ToBeVisibleAsync();
        await Expect(shell.ContextSwitcher).ToContainTextAsync(culture == "ar" ? names.SchoolGroupAr : names.SchoolGroupEn);
        await AssertDirectionAsync(culture);
    }

    [Theory(Skip = NeedsClientsAndLevelTypesApi)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task School_admin_builds_level_types(string culture)
    {
        // First part of the plan's School_admin_builds_level_types_and_tree: School › Stage › Section.
        var names = M1Names.New();
        await StartAsync(culture: culture);
        await SignInAsPlatformAdminAsync();
        var clients = new ClientsAdminPage(Page, culture);
        await clients.OpenAsync();
        await clients.CreateAsync(SchoolGroup(names));
        await SignOutAsync();
        await InvitedAdminSignsInAsync(names.AdminEmail("school"), names.NewPassword);

        var levels = new LevelTypesAdminPage(Page, culture);
        await levels.OpenAsync();
        await levels.AddAsync("مرحلة", "Stage", canHostClasses: false);
        await levels.AddAsync("فصل", "Section", canHostClasses: true);

        string[] expected = culture == "ar" ? ["مدرسة", "مرحلة", "فصل"] : ["School", "Stage", "Section"];
        Assert.Equal(expected, await levels.NamesInOrderAsync());
        await AssertDirectionAsync(culture);
    }

    private static ClientsAdminPage.NewClient SchoolGroup(M1Names n) => new(
        n.SchoolGroupAr, n.SchoolGroupEn, "SchoolGroup", "مدرسة", "School", n.RootCode("SCH"),
        "مدير المدرسة", "School Admin", n.AdminEmail("school"));

    private static ClientsAdminPage.NewClient University(M1Names n) => new(
        n.UniversityAr, n.UniversityEn, "University", "جامعة", "University", n.RootCode("UNI"),
        "مدير الجامعة", "University Admin", n.AdminEmail("uni"));

    private async Task SignInAsPlatformAdminAsync()
    {
        await new LoginPage(Page).SignInAsync(Settings.PlatformAdminEmail, Settings.Password);
        await new ShellPage(Page).WaitReadyAsync();
    }

    private async Task InvitedAdminSignsInAsync(string email, string password)
    {
        using var mail = new MailpitClient();
        var link = await mail.WaitForAccountLinkAsync(email);
        await new ResetPasswordPage(Page).SetPasswordAsync(link, password);
        await new LoginPage(Page).SignInAsync(email, password);
        await new ShellPage(Page).WaitReadyAsync();
    }

    /// <summary>Signs out of the app and the identity server so the next sign-in starts clean.</summary>
    private async Task SignOutAsync()
    {
        await Page.Context.ClearCookiesAsync();
        await Page.EvaluateAsync("() => { sessionStorage.clear(); localStorage.removeItem('lms.context'); }");
    }

    private async Task AssertDirectionAsync(string culture) =>
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
}
