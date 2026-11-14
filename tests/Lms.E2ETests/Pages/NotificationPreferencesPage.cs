using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>Notification preferences (/notifications/preferences, M6 FE2; ids from docs/m6-e2e-testids-fe2.md).</summary>
public sealed class NotificationPreferencesPage(IPage page)
{
    public ILocator Root => page.GetByTestId("prefs");
    public ILocator Cell(string eventKey, string channel) => page.GetByTestId($"prefs-{eventKey}-{channel}");
    public ILocator Source(string eventKey, string channel) => page.GetByTestId($"prefs-source-{eventKey}-{channel}");
    public ILocator Bar => page.GetByTestId("prefs-bar");
    public ILocator PushConsent => page.GetByTestId("push-consent");

    public async Task OpenAsync()
    {
        await page.GotoAsync("/notifications/preferences");
        await Root.WaitForAsync();
    }

    public Task SaveAsync() => page.GetByTestId("prefs-save").ClickAsync();

    public Task DiscardAsync() => page.GetByTestId("prefs-discard").ClickAsync();
}
