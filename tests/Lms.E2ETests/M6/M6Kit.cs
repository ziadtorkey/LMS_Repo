using System.Text.Json;
using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M6;

/// <summary>
/// One signed-in person in their own browser context (M6 Task 34): realtime and the D69 scenarios need two or three people at once (a student and a teacher, a
/// teacher and two guardians). The context starts in the given culture and width, with touch for the phone case, and records its own trace like
/// <see cref="E2ETestBase"/> does. Dispose it (<c>await using</c>) at the end of the scenario.
/// </summary>
public sealed class UserSession : IAsyncDisposable
{
    private readonly IBrowserContext _context;
    private readonly string _artifacts;

    private UserSession(IBrowserContext context, IPage page, E2EApi api, string artifacts, string who)
    {
        _context = context;
        Page = page;
        Api = api;
        _artifacts = artifacts;
        Who = who;
        Messages = new MessagesPage(page);
        NewConversation = new NewConversationDialog(page);
    }

    public string Who { get; }
    public IPage Page { get; }
    public E2EApi Api { get; }
    public MessagesPage Messages { get; }
    public NewConversationDialog NewConversation { get; }

    /// <summary>Opens a context, signs in as the seeded user (<c>E2ESettings.SeedUser</c>) and waits for the shell and the active context.</summary>
    public static async Task<UserSession> OpenAsync(
        E2EFixture fixture, string seedUser, string culture, int width = 1280, int height = 800, bool touch = false, string test = "")
    {
        var artifacts = Path.Combine(RepoPaths.Root, "TestResults", "e2e", $"{test}-{seedUser}-{culture}");
        Directory.CreateDirectory(artifacts);
        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = fixture.Settings.BaseUrl.ToString(),
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
            Locale = culture == "ar" ? "ar-EG" : "en-US",
            HasTouch = touch,
            IsMobile = touch,
        });
        await context.AddInitScriptAsync($"if (!localStorage.getItem('lms.culture')) localStorage.setItem('lms.culture', '{culture}');");
        context.SetDefaultTimeout(E2ETestBase.DefaultTimeoutMs);
        await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = false });
        var page = await context.NewPageAsync();

        await new LoginPage(page).SignInAsync(E2ESettings.SeedUser(seedUser), fixture.Settings.Password);
        var shell = new ShellPage(page);
        await shell.WaitReadyAsync();
        await page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        return new UserSession(context, page, new E2EApi(page, fixture.Settings.BaseUrl), artifacts, seedUser);
    }

    /// <summary>The signed-in person's user id (what the picker and the participants use), from <c>GET /api/me</c>.</summary>
    public async Task<Guid> UserIdAsync()
    {
        var me = await Api.JsonAsync(await Api.SendAsync("GET", "/api/me"));
        return me.GetProperty("userId").GetGuid();
    }

    /// <summary>The API answer for a write the UI does not offer (a forced post): the server is the authority, never the hidden button.</summary>
    public async Task<(int Status, JsonElement? Problem)> ForcePostAsync(Guid conversationId, string text)
    {
        var response = await Api.SendAsync("POST", $"/api/conversations/{conversationId}/messages", new { body = text, attachmentFileIds = (Guid[]?)null });
        var problem = response.Status >= 400 ? await Api.JsonAsync(response) : (JsonElement?)null;
        return (response.Status, problem);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(_artifacts, "final.png"), FullPage = true });
        }
        catch (PlaywrightException)
        {
            // The page may already be gone; the trace has everything up to that point.
        }

        await _context.Tracing.StopAsync(new TracingStopOptions { Path = Path.Combine(_artifacts, "trace.zip") });
        await _context.DisposeAsync();
    }

    public Task AssertDirectionAsync(string culture) =>
        Expect(Page.Locator("html")).ToHaveAttributeAsync("dir", culture == "ar" ? "rtl" : "ltr");

    public Task GoOfflineAsync() => _context.SetOfflineAsync(true);

    public Task GoOnlineAsync() => _context.SetOfflineAsync(false);
}

/// <summary>
/// The M6 messaging world the scenarios assume (M6 Task 34). The people are seeded users of the demo client (<c>E2ESettings.SeedUser</c>), created by the
/// M6 demo seed (BE2, with the M6 backend): they must exist with these relationships, the Messaging flag on for the client and the D69 default rules.
/// </summary>
public static class MessagingWorld
{
    /// <summary>A student in class 10-A, taught by <see cref="Teacher"/> (class course X, group 1).</summary>
    public const string Student1 = "student1";

    /// <summary>A second student of the same class course (group 2): never reachable by student 1 (D69).</summary>
    public const string Student2 = "student2";

    /// <summary>Guardian of student 1 (and only student 1).</summary>
    public const string Guardian1 = "guardian1";

    /// <summary>Guardian of student 2: another guardian that guardian 1 must not reach.</summary>
    public const string Guardian2 = "guardian2";

    /// <summary>Teacher of class course X: reaches students and guardians of X, staff of the unit.</summary>
    public const string Teacher = "teacher";

    /// <summary>A manager of the school: reaches staff and guardians; a student only through a group or an announcement.</summary>
    public const string Manager = "manager";

    /// <summary>Org Admin of the client: edits the messaging rules (tighten only).</summary>
    public const string OrgAdmin = "orgadmin";

    /// <summary>The server's delete-for-everyone window (D66: 15 minutes), <c>Messaging.EditWindowMinutes</c>.</summary>
    public const int DeleteWindowMinutes = 15;
}
