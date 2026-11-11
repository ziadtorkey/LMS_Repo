using System.Runtime.CompilerServices;
using Microsoft.Playwright;

namespace Lms.E2ETests.Infrastructure;

/// <summary>
/// Gives each test a fresh browser context (clean localStorage → no stored culture or context) and records a
/// Playwright trace plus a final screenshot under TestResults/e2e/&lt;test&gt;/ for diagnosing failures in CI.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
public abstract class E2ETestBase(E2EFixture fixture) : IAsyncLifetime
{
    public const float DefaultTimeoutMs = 20_000;

    private IBrowserContext? _context;
    private string _artifacts = string.Empty;

    protected E2EFixture Fixture { get; } = fixture;
    protected E2ESettings Settings => Fixture.Settings;
    protected IPage Page { get; private set; } = null!;

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Opens a new context; call first in every test. Width 360 for the phone layout checks. With a
    /// <paramref name="culture"/> ("ar" or "en") the browser prefers that language (server pages via Accept-Language)
    /// and the app starts in it (stored <c>lms.culture</c>, set only if absent so a later switch still works).
    /// </summary>
    protected async Task StartAsync(
        int width = 1280, int height = 800, string? culture = null, [CallerMemberName] string test = "")
    {
        _artifacts = Path.Combine(RepoPaths.Root, "TestResults", "e2e", culture is null ? test : $"{test}-{culture}");
        Directory.CreateDirectory(_artifacts);

        _context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Settings.BaseUrl.ToString(),
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
            Locale = culture switch { "en" => "en-US", "ar" => "ar-EG", _ => null },
        });
        if (culture is not null)
        {
            await _context.AddInitScriptAsync(
                $"if (!localStorage.getItem('lms.culture')) localStorage.setItem('lms.culture', '{culture}');");
        }

        _context.SetDefaultTimeout(DefaultTimeoutMs);
        await _context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = false });
        Page = await _context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        if (_context is null)
        {
            return;
        }

        try
        {
            await Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(_artifacts, "final.png"), FullPage = true });
        }
        catch (PlaywrightException)
        {
            // The page may already be gone; the trace still has everything up to that point.
        }

        await _context.Tracing.StopAsync(new TracingStopOptions { Path = Path.Combine(_artifacts, "trace.zip") });
        await _context.DisposeAsync();
    }
}
