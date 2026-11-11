using System.Text.Json;
using Microsoft.Playwright;

namespace Lms.E2ETests.Infrastructure;

/// <summary>
/// Runs a background job on demand through the Development-only trigger <c>POST /api/dev/jobs/{id}/run?asOf=</c>
/// (BE #201). It needs a platform admin, so it signs in as one in a throw-away browser context, reads the access
/// token the Blazor client keeps in session storage and calls the API with it; the response returns after the job finished.
/// </summary>
public static class JobsClient
{
    public static async Task RunAsync(E2EFixture fixture, string jobId, DateOnly? asOf = null)
    {
        var settings = fixture.Settings;
        await using var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = settings.BaseUrl.ToString(),
            IgnoreHTTPSErrors = true,
        });
        context.SetDefaultTimeout(E2ETestBase.DefaultTimeoutMs);
        var page = await context.NewPageAsync();
        await new Pages.LoginPage(page).SignInAsync(settings.PlatformAdminEmail, settings.Password);
        await new Pages.ShellPage(page).WaitReadyAsync();

        var token = await AccessTokenAsync(page);
        var url = $"/api/dev/jobs/{Uri.EscapeDataString(jobId)}/run" + (asOf is { } day ? $"?asOf={day:yyyy-MM-dd}" : string.Empty);
        var response = await context.APIRequest.PostAsync(url, new APIRequestContextOptions
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
        if (!response.Ok)
        {
            throw new InvalidOperationException($"Running job '{jobId}' failed: {response.Status} {await response.TextAsync()}");
        }
    }

    /// <summary>The OIDC user the client stores under <c>oidc.user:{authority}:{client}</c> in session storage.</summary>
    private static async Task<string> AccessTokenAsync(IPage page)
    {
        var json = await page.EvaluateAsync<string?>(
            "() => { const k = Object.keys(sessionStorage).find(x => x.startsWith('oidc.user:')); return k ? sessionStorage.getItem(k) : null; }")
            ?? throw new InvalidOperationException("No signed-in user was found in session storage (oidc.user:*).");
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("access_token").GetString()
               ?? throw new InvalidOperationException("The stored user has no access token.");
    }
}
