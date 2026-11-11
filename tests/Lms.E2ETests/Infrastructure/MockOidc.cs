using System.Text.Json;
using Microsoft.Playwright;

namespace Lms.E2ETests.Infrastructure;

/// <summary>
/// The <c>mock-oidc</c> compose service (navikt/mock-oauth2-server, interactive login). Its sign-in page takes any user name,
/// which becomes the token's <c>sub</c>, and an optional claims JSON: that is how a test chooses the next login's identity.
/// The issuer is <c>http://localhost:8090/client-a</c>: the browser and the API both reach the server by that name, because
/// the server builds <c>iss</c> from the request host.
/// </summary>
public static class MockOidc
{
    public static string Authority { get; } =
        (Environment.GetEnvironmentVariable("LMS_E2E_MOCK_OIDC") ?? "http://localhost:8090").TrimEnd('/') + "/client-a";

    /// <summary>The client id and secret registered for the provider under test: the mock accepts any values.</summary>
    public const string ClientId = "lms-e2e";
    public const string ClientSecret = "lms-e2e-secret";

    /// <summary>Waits for the mock's sign-in form (the page is already redirected there) and signs in as the given identity.</summary>
    public static async Task SignInAsync(IPage page, string subject, string email)
    {
        await page.WaitForURLAsync(url => url.StartsWith(Authority, StringComparison.OrdinalIgnoreCase));
        await page.Locator("input[name='username']").FillAsync(subject);
        await page.Locator("textarea[name='claims']").FillAsync(JsonSerializer.Serialize(new { email }));
        await page.Locator("input[type='submit']").ClickAsync();
        await page.WaitForURLAsync(url => !url.StartsWith(Authority, StringComparison.OrdinalIgnoreCase));
    }
}
