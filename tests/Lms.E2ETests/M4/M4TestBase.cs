using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.M1;
using Lms.E2ETests.M2;
using Lms.E2ETests.M3;
using Lms.E2ETests.Pages;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M4;

/// <summary>
/// Shared set-up of the M4a demo scenarios (plan Task 32): the data comes from <see cref="AssessmentKit"/> through the API as the
/// Org Admin; a student and the seeded teacher then sign in from their own browser contexts so each keeps its session.
/// </summary>
public abstract class M4TestBase : E2ETestBase
{
    protected M4TestBase(E2EFixture fixture) : base(fixture)
    {
        Assertions.SetDefaultExpectTimeout(30_000); // the WASM app takes ~11 s to boot on a cold page
    }

    /// <summary>One signed-in browser context (the page of a student or of the teacher).</summary>
    protected sealed class Session(IBrowserContext context, IPage page, string name, string artifacts) : IAsyncDisposable
    {
        public IPage Page { get; } = page;

        public E2EApi Api(Uri baseUrl) => new(Page, baseUrl);

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(artifacts, $"final-{name}.png"), FullPage = true });
            }
            catch (PlaywrightException)
            {
                // The page may already be gone.
            }

            await context.DisposeAsync();
        }
    }

    private string ArtifactsDir => Path.Combine(RepoPaths.Root, "TestResults", "e2e", $"{GetType().Name}-secondary");

    /// <summary>Signs in as the Org Admin in the base context and creates the world; returns the admin's API.</summary>
    protected async Task<(AssessmentKit.World World, E2EApi Admin, M2Names Names)> StartWorldAsync(string culture)
    {
        var names = M2Names.New();
        await StartAsync(culture: culture);
        await M1Kit.SignInAsync(Page, E2ESettings.SeedUser("orgadmin"), Settings.Password, culture);
        await Expect(new ShellPage(Page).ContextSwitcher).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        var admin = new E2EApi(Page, Settings.BaseUrl);
        var world = await AssessmentKit.CreateWorldAsync(admin, names);
        return (world, admin, names);
    }

    /// <summary>The student of the world: first sign-in with the default password, then the forced change.</summary>
    protected async Task<Session> OpenStudentAsync(AssessmentKit.World world, M2Names names, string culture)
    {
        var (context, page) = await NewPageAsync(culture);
        await ChangePasswordPage.FirstSignInAsync(page, world.Student.UserName, world.Student.Password, names.NewPassword);
        await WaitContextAsync(page);
        return new Session(context, page, "student", ArtifactsDir);
    }

    /// <summary>
    /// The grader of the scenarios. The seeded Teacher is assigned to the class course, but the shared E2E academic year starts on
    /// 2027-09-01 and a teacher assignment cannot start before its year, so the teacher relationship is not current yet and the
    /// teacher sees the class course as 404. The Org Admin holds the same grading permissions, so signs in instead; switch to
    /// <c>E2ESettings.SeedUser("teacher")</c> once the E2E year contains today (issue #455 tracks the seed; remove this workaround then).
    /// </summary>
    protected async Task<Session> OpenTeacherAsync(string culture)
    {
        var (context, page) = await NewPageAsync(culture);
        await M1Kit.SignInAsync(page, E2ESettings.SeedUser("orgadmin"), Settings.Password, culture);
        await WaitContextAsync(page);
        return new Session(context, page, "teacher", ArtifactsDir);
    }

    private static async Task WaitContextAsync(IPage page)
    {
        // The context is picked after the shell renders; a page opened by URL before that answers 403 (see #275).
        await Expect(new ShellPage(page).ContextSwitcher).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
    }

    private async Task<(IBrowserContext Context, IPage Page)> NewPageAsync(string culture)
    {
        Directory.CreateDirectory(ArtifactsDir);
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
        var log = Path.Combine(ArtifactsDir, "console.log"); // errors the browser reported: the first place to look when a page misbehaves
        page.Console += (_, m) => { if (m.Type is "error" or "warning") File.AppendAllText(log, $"[{m.Type}] {m.Text}{Environment.NewLine}"); };
        page.PageError += (_, e) => File.AppendAllText(log, $"[pageerror] {e}{Environment.NewLine}");
        page.Response += async (_, r) =>
        {
            if (r.Status < 400 || r.Url.Contains("blazor.boot.json", StringComparison.Ordinal)) return;
            string body;
            try { body = await r.TextAsync(); } catch { body = "?"; }
            body = body.ReplaceLineEndings(" ");
            File.AppendAllText(log, $"[http {r.Status}] {r.Request.Method} {r.Url} {body[..Math.Min(300, body.Length)]}{Environment.NewLine}");
        };
        return (context, page);
    }

    /// <summary>The module settings every API-created activity uses: published for the whole class course, the completion rule of the activity.</summary>
    protected static object ModuleSettings(string titleAr, string titleEn, string completionRule) => new
    {
        titleAr, titleEn, visibility = "Published", visibleFromUtc = (DateTime?)null, visibleToUtc = (DateTime?)null,
        target = "ClassCourse", groupId = (Guid?)null, completionRule, completionMinScore = (decimal?)null,
    };

    protected async Task AssertDirectionAsync(IPage page, string culture) =>
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(page).HtmlDirAsync());
}
