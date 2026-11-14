using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.M1;
using Lms.E2ETests.M2;
using Lms.E2ETests.M4;
using Lms.E2ETests.Pages;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M5;

/// <summary>
/// Shared set-up of the M5 exit demos (plan #621 Tasks 42-44). It reuses the M4a world (<see cref="AssessmentKit"/>: school, class course with a
/// published lesson plan, the seeded teacher assigned, an enrolled student) and the session helpers of <see cref="M4TestBase"/> (a second browser
/// context per role; the Org Admin acts as the grader until the E2E year is current for the seeded teacher, #455). M5 adds <see cref="M5Kit"/> (API
/// helpers), <see cref="LeakProbe"/> (whole-page leak check) and the guardian set-up.
/// </summary>
public abstract class M5TestBase(E2EFixture fixture) : M4TestBase(fixture)
{
    // Why each scenario is skipped: named PRs, so un-skipping is mechanical (remove a constant when its PRs are on main).
    protected const string NeedsGradebookGrid =
        "Needs on main: scales (#641, #647), gradebook model, API and grid (#638, #650), grid and editing pages (#660, #663), settings panel (#664)";

    protected const string NeedsPublishing = NeedsGradebookGrid + ", publishing (#656) and the student/guardian reads and pages (Task 9, #662)";

    protected const string NeedsShellAndScales = "Needs the gradebook tab (#630) and the grading scales API (#641, #625) on main";

    /// <summary>The world with the guardian portal (D30, default off) switched on for the school BEFORE the student is created, so the guardian is invited.</summary>
    protected async Task<(AssessmentKit.World World, E2EApi Admin, M2Names Names, Guid School)> StartWorldWithPortalAsync(string culture)
    {
        var names = M2Names.New();
        await StartAsync(culture: culture);
        await M1Kit.SignInAsync(Page, E2ESettings.SeedUser("orgadmin"), Settings.Password, culture);
        await Expect(new ShellPage(Page).ContextSwitcher).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        var admin = new E2EApi(Page, Settings.BaseUrl);
        var school = await M5Kit.SchoolUnitAsync(admin);
        await M5Kit.SetGuardianPortalAsync(admin, school, on: true);
        var world = await AssessmentKit.CreateWorldAsync(admin, names);
        return (world, admin, names, school);
    }

    /// <summary>The invited guardian of the world's student: activates from the e-mail, sets a password and signs in, in a context of their own.</summary>
    protected async Task<Session> OpenGuardianAsync(M2Names names, string culture)
    {
        var artifacts = Path.Combine(RepoPaths.Root, "TestResults", "e2e", $"{GetType().Name}-secondary");
        Directory.CreateDirectory(artifacts);
        var context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Settings.BaseUrl.ToString(),
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            IgnoreHTTPSErrors = true,
            Locale = culture == "en" ? "en-US" : "ar-EG",
        });
        await context.AddInitScriptAsync($"if (!localStorage.getItem('lms.culture')) localStorage.setItem('lms.culture', '{culture}');");
        context.SetDefaultTimeout(DefaultTimeoutMs);
        var page = await context.NewPageAsync();

        var email = M5Kit.GuardianEmail(names);
        using var mail = new MailpitClient();
        var link = await mail.WaitForAccountLinkAsync(email);
        await new ResetPasswordPage(page).SetPasswordAsync(link, names.NewPassword);
        await M1Kit.SignInAsync(page, email, names.NewPassword, culture);
        await Expect(new ShellPage(page).ContextSwitcher).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        return new Session(context, page, "guardian", artifacts);
    }
}
