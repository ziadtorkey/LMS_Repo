using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The messaging rules matrix (/settings/messaging-rules, M6 FE2; ids from docs/m6-e2e-testids-fe2.md).</summary>
public sealed class MessagingRulesPage(IPage page)
{
    public ILocator Title => page.GetByTestId("rules-title");
    public ILocator Matrix => page.GetByTestId("rules-matrix");
    public ILocator Cell(string from, string to) => page.GetByTestId($"rules-{from}-{to}");
    public ILocator Bar => page.GetByTestId("rules-bar");
    public ILocator Refused => page.GetByTestId("rules-refused");
    public ILocator Error => page.GetByTestId("rules-error");

    public async Task OpenAsync()
    {
        await page.GotoAsync("/settings/messaging-rules");
        await Title.WaitForAsync();
    }

    public Task SaveAsync() => page.GetByTestId("rules-save").ClickAsync();

    public Task DiscardAsync() => page.GetByTestId("rules-discard").ClickAsync();
}
