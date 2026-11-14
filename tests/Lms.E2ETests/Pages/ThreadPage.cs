using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The messages page, an open thread, its composer and the block controls (FE1 #761).</summary>
public sealed class ThreadPage(IPage page)
{
    public ILocator Thread => page.GetByTestId("thread");
    public ILocator Composer => page.GetByTestId("composer-text");
    public ILocator Send => page.GetByTestId("composer-send");
    public ILocator ComposerUnblock => page.GetByTestId("composer-unblock");
    public ILocator Message(Guid id) => page.GetByTestId($"msg-{id}");
    public ILocator BlockedCard => page.GetByTestId("blocked-card");

    public async Task OpenAsync(Guid conversationId)
    {
        await page.GotoAsync($"/messages/{conversationId}");
        await Thread.WaitForAsync();
    }

    public async Task SendAsync(string text)
    {
        await Composer.FillAsync(text);
        await Send.ClickAsync();
    }

    /// <summary>Blocks the other person from the thread menu and confirms.</summary>
    public async Task BlockAsync()
    {
        await page.GetByTestId("thread-more").ClickAsync();
        await page.GetByTestId("thread-block").ClickAsync();
        await page.GetByTestId("block-confirm").ClickAsync();
        await page.GetByTestId("block").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    public Task UnblockFromComposerAsync() => ComposerUnblock.ClickAsync();
}
