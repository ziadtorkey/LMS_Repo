using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>Communication settings (/settings/communication, M6 FE2; ids from docs/m6-e2e-testids-fe2.md).</summary>
public sealed class CommunicationSettingsPage(IPage page)
{
    public ILocator Title => page.GetByTestId("comms-title");
    public ILocator Field(string name) => page.GetByTestId($"comms-{name}");
    public ILocator QuietOn => page.GetByTestId("comms-quiet-on");
    public ILocator Bar => page.GetByTestId("comms-bar");
    public ILocator Error => page.GetByTestId("comms-error");

    public async Task OpenAsync()
    {
        await page.GotoAsync("/settings/communication");
        await Title.WaitForAsync();
    }

    public Task SaveAsync() => page.GetByTestId("comms-save").ClickAsync();

    public Task DiscardAsync() => page.GetByTestId("comms-discard").ClickAsync();
}
