using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.Pages;

/// <summary>The audit viewer (/audit): filters, the entry table (newest first), the before/after drawer and the exports.</summary>
public sealed class AuditPage(IPage page)
{
    public ILocator Drawer => page.GetByTestId("audit-drawer");

    public ILocator Diff(string property) => page.GetByTestId($"diff-{property}");

    public async Task GotoAsync()
    {
        await page.GotoAsync("/audit");
        await page.GetByTestId("filter-entity").WaitForAsync();
    }

    public async Task FilterAsync(string entityText, string actionText)
    {
        await MudControls.SelectAsync(page, page.GetByTestId("filter-entity"), entityText);
        await MudControls.SelectAsync(page, page.GetByTestId("filter-action"), actionText);
        // The table reloads on each filter change; wait until the newest row shows the chosen entity.
        await Expect(page.Locator("[data-testid^='audit-open-']").First).ToContainTextAsync(entityText);
    }

    /// <summary>Opens the newest entry in the table and waits for its diff.</summary>
    public async Task OpenNewestAsync()
    {
        await page.Locator("[data-testid^='audit-open-']").First.ClickAsync();
        await page.GetByTestId("audit-diff").WaitForAsync();
    }

    /// <summary>Clicks an export button and returns the downloaded file's bytes and suggested name.</summary>
    public async Task<(byte[] Content, string FileName)> ExportAsync(string testId)
    {
        var download = await page.RunAndWaitForDownloadAsync(() => page.GetByTestId(testId).ClickAsync());
        var path = await download.PathAsync();
        return (await File.ReadAllBytesAsync(path), download.SuggestedFilename);
    }
}
