using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.M1;
using Lms.E2ETests.M2;
using Lms.E2ETests.Pages;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M3;

/// <summary>
/// M3 exit demo 2 (plan Task 47, #255), self-service part: a student edits what is theirs directly, asks for a change of a
/// critical field, and a Counselor decides. Three browser contexts: the admin (set-up and checks through the API, the base
/// context), the student, and the counselor.
/// </summary>
public sealed class ChangeRequestTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Student_edits_nickname_directly_requests_name_change_counselor_approves_student_sees_new_name_and_notification(string culture)
    {
        var names = M2Names.New();
        await using var student = await StartStudentAsync(names, culture);
        var newNameEn = names.NameEn("Renamed");
        var newNameAr = names.NameAr("مُعدَّل");
        var nickname = names.NameEn("Nick");

        // A direct field is saved at once; a critical one goes to the Counselor as a request.
        await OpenOwnProfileAsync(student.Page);
        await Field(student.Page, "NicknameEn").FillAsync(nickname);
        await student.Page.GetByTestId("my-save").ClickAsync();
        await Expect(student.Page.Locator(".mud-snackbar")).ToBeVisibleAsync();
        Assert.Equal(nickname, (await DetailAsync(student.Account.Id)).GetProperty("nicknameEn").GetString());

        await RequestAsync(student.Page, new Dictionary<string, string> { ["NameEn"] = newNameEn, ["NameAr"] = newNameAr });
        await Expect(student.Page.Locator("[data-testid^='request-status-']").First).ToHaveTextAsync(Resx.In(culture, "ChangeRequestStatus_Pending"));
        await Expect(student.Page.GetByTestId("pending-message")).ToBeVisibleAsync();

        // The student keeps the page open: the decision reaches the bell live, without a reload.
        await student.Page.EvaluateAsync("() => { window.__lmsNoReload = true; }");
        var badge = student.Page.GetByTestId("notif-badge").Locator(".mud-badge");

        // The Counselor sees the request with the before and after of each field and approves it.
        await using var counselor = await OpenCounselorAsync(culture);
        await OpenRequestAsync(counselor.Page, student.Account, culture);
        await Expect(counselor.Page.GetByTestId("before-NameEn")).ToContainTextAsync(student.Account.NameEn);
        await Expect(counselor.Page.GetByTestId("after-NameEn")).ToContainTextAsync(newNameEn);
        await counselor.Page.GetByTestId("decision-approve").ClickAsync();
        await Expect(counselor.Page.Locator(".mud-dialog")).ToHaveCountAsync(0);

        // The student sees the decision in the bell with no reload, and the new name once the page is opened again.
        await Expect(badge).ToHaveTextAsync("1");
        Assert.True(await student.Page.EvaluateAsync<bool>("() => window.__lmsNoReload === true"), "the student's page was reloaded");
        await OpenOwnProfileAsync(student.Page);
        await Expect(student.Page.Locator("[data-testid^='request-status-']").First).ToHaveTextAsync(Resx.In(culture, "ChangeRequestStatus_Approved"));
        Assert.Equal(newNameEn, (await DetailAsync(student.Account.Id)).GetProperty("summary").GetProperty("nameEn").GetString());
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Rejected_request_shows_comment_to_student(string culture)
    {
        var names = M2Names.New();
        await using var student = await StartStudentAsync(names, culture);
        const string comment = "Bring the birth certificate first";

        await OpenOwnProfileAsync(student.Page);
        await RequestAsync(student.Page, new Dictionary<string, string> { ["NameEn"] = names.NameEn("Wanted") });

        await using var counselor = await OpenCounselorAsync(culture);
        await OpenRequestAsync(counselor.Page, student.Account, culture);
        // Rejecting needs a reason: the first click only asks for it.
        await counselor.Page.GetByTestId("decision-reject").ClickAsync();
        await counselor.Page.Locator("textarea[data-testid='decision-comment'], [data-testid='decision-comment'] textarea").Locator("visible=true").First.FillAsync(comment);
        await counselor.Page.GetByTestId("decision-reject").ClickAsync();
        await Expect(counselor.Page.Locator(".mud-dialog")).ToHaveCountAsync(0);

        await OpenOwnProfileAsync(student.Page);
        var table = student.Page.GetByTestId("requests-table");
        await Expect(table).ToContainTextAsync(Resx.In(culture, "ChangeRequestStatus_Rejected"));
        await Expect(table).ToContainTextAsync(comment);
        Assert.Equal(student.Account.NameEn, (await DetailAsync(student.Account.Id)).GetProperty("summary").GetProperty("nameEn").GetString()); // unchanged
    }

    /// <summary>The self-service fields are inputs with <c>field-&lt;Name&gt;</c> test ids (on the input or a wrapper).</summary>
    private static ILocator Field(IPage page, string name) =>
        page.Locator($"input[data-testid='field-{name}'], [data-testid='field-{name}'] input").First;

    private static async Task OpenOwnProfileAsync(IPage page)
    {
        await page.GotoAsync("/me/profile");
        await page.GetByTestId("my-profile-title").WaitForAsync();
    }

    /// <summary>Fills the given critical fields in the request dialog and sends it.</summary>
    private static async Task RequestAsync(IPage page, IReadOnlyDictionary<string, string> changes)
    {
        await page.GetByTestId("request-change").ClickAsync();
        var dialog = page.Locator(".mud-dialog");
        foreach (var (field, value) in changes)
        {
            await Field(page, field).FillAsync(value);
        }

        await dialog.GetByTestId("dialog-save").ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
    }

    /// <summary>Opens the change-request queue as the counselor and the review dialog of the student's request.</summary>
    private static async Task OpenRequestAsync(IPage page, M3Kit.StudentAccount student, string culture)
    {
        await page.GotoAsync("/student-change-requests");
        await page.GetByTestId("queue-title").WaitForAsync();
        var row = page.Locator("tr").Filter(new LocatorFilterOptions { HasText = culture == "ar" ? student.NameAr : student.NameEn });
        await row.Locator("[data-testid^='queue-open-']").ClickAsync();
        await page.GetByTestId("detail-student").WaitForAsync();
    }

    private async Task<System.Text.Json.JsonElement> DetailAsync(Guid id)
    {
        var api = new E2EApi(Page, Settings.BaseUrl);
        return await api.JsonAsync(await api.SendAsync("GET", $"/api/students/{id}"));
    }

    /// <summary>A second browser context with its own session, in the language of the test.</summary>
    private async Task<IBrowserContext> NewContextAsync(string culture)
    {
        var context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Settings.BaseUrl.ToString(),
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            IgnoreHTTPSErrors = true,
            Locale = culture == "en" ? "en-US" : "ar-EG",
        });
        await context.AddInitScriptAsync($"if (!localStorage.getItem('lms.culture')) localStorage.setItem('lms.culture', '{culture}');");
        context.SetDefaultTimeout(DefaultTimeoutMs);
        return context;
    }

    private sealed class Session(IBrowserContext context, IPage page, string name, string artifacts, M3Kit.StudentAccount? account = null) : IAsyncDisposable
    {
        public IPage Page { get; } = page;

        public M3Kit.StudentAccount Account { get; } = account!;

        /// <summary>Keeps a final screenshot of this session next to the base context's artifacts (the failure is often on the other side).</summary>
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

    /// <summary>
    /// Sets up in the base context as the Org Admin (a student at KG Section 1 with a guardian, through the API), then signs
    /// the student in from a second context: first sign-in with the default password, then the forced change.
    /// </summary>
    private string ArtifactsDir => Path.Combine(RepoPaths.Root, "TestResults", "e2e", $"{nameof(ChangeRequestTests)}-secondary");

    private async Task<Session> StartStudentAsync(M2Names names, string culture)
    {
        await StartAsync(culture: culture);
        await M1Kit.SignInAsync(Page, E2ESettings.SeedUser("orgadmin"), Settings.Password, culture);
        var api = new E2EApi(Page, Settings.BaseUrl);
        var kg = await M3Kit.EnsureKgSectionAsync(api);
        var account = await M3Kit.CreateStudentAsync(api, kg, names, "Student", slot: 1);

        Directory.CreateDirectory(ArtifactsDir);
        var context = await NewContextAsync(culture);
        var page = await context.NewPageAsync();
        await ChangePasswordPage.FirstSignInAsync(page, account.UserName, account.Password, names.NewPassword);
        await Expect(new ShellPage(page).ContextSwitcher).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        return new Session(context, page, "student", ArtifactsDir, account);
    }

    private async Task<Session> OpenCounselorAsync(string culture)
    {
        var context = await NewContextAsync(culture);
        var page = await context.NewPageAsync();
        await M1Kit.SignInAsync(page, E2ESettings.SeedUser("counselor"), Settings.Password, culture);
        // The context is picked after the shell renders; a page opened by URL before that answers 403 (see #275).
        await Expect(new ShellPage(page).ContextSwitcher).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        return new Session(context, page, "counselor", ArtifactsDir);
    }
}
