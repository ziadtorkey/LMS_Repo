using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The announcements list for readers (feed tab) and authors (manage tab), /announcements (M6 FE2; ids verified against the pages and docs/m6-e2e-testids-fe2.md).</summary>
public sealed class AnnouncementsFeedPage(IPage page)
{
    public ILocator Title => page.GetByTestId("anns-title");
    public ILocator Feed => page.GetByTestId("anns-feed");
    public ILocator Cards => page.Locator("[data-testid^='ann-card-']");
    public ILocator Empty => page.GetByTestId("anns-empty");
    public ILocator Error => page.GetByTestId("anns-error");
    public ILocator ManageTable => page.GetByTestId("anns-table");

    public ILocator Card(Guid id) => page.GetByTestId($"ann-card-{id}");
    public ILocator Unread(Guid id) => page.GetByTestId($"ann-unread-{id}");
    public ILocator Row(Guid id) => page.GetByTestId($"ann-row-{id}");
    public ILocator RowStatus(Guid id) => page.GetByTestId($"ann-status-{id}");

    public async Task OpenAsync()
    {
        await page.GotoAsync("/announcements");
        await Title.WaitForAsync();
    }

    public async Task OpenManageAsync()
    {
        await OpenAsync();
        await page.GetByTestId("anns-tab-manage").ClickAsync();
        await ManageTable.WaitForAsync();
    }

    public Task NewAsync() => page.GetByTestId("anns-new").ClickAsync();

    public Task MarkReadAsync(Guid id) => page.GetByTestId($"ann-read-{id}").ClickAsync();

    public Task OpenDetailAsync(Guid id) => page.GetByTestId($"ann-open-{id}").ClickAsync();

    public Task PublishAsync(Guid id) => page.GetByTestId($"ann-publish-{id}").ClickAsync();

    public Task WithdrawAsync(Guid id) => page.GetByTestId($"ann-withdraw-{id}").ClickAsync();

    /// <summary>The visible titles in feed order (the card heading), for "appears once" and ordering checks.</summary>
    public async Task<IReadOnlyList<string>> TitlesAsync() =>
        [.. (await Cards.Locator("h1, h2, h3, h4, h5, h6").AllInnerTextsAsync()).Select(t => t.Trim())];
}
