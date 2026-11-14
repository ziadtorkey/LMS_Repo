using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.Pages;

/// <summary>The publish dialog of the gradebook (M5 Task 27, #721): pick items, choose Now, Schedule or Hide, acknowledge a provisional scale. Ids are the ones in #721's dialog.</summary>
public sealed class PublishItemsDialog(IPage page)
{
    public ILocator Pick(Guid itemId) => page.GetByTestId($"publish-pick-{itemId}");

    public ILocator ModeNow => page.GetByTestId("publish-mode-now");

    public ILocator ModeSchedule => page.GetByTestId("publish-mode-schedule");

    public ILocator ModeHide => page.GetByTestId("publish-mode-hide");

    public ILocator ProvisionalAcknowledgement => page.GetByTestId("publish-ack");

    public ILocator Submit => page.GetByTestId("publish-submit");

    public ILocator Cancel => page.GetByTestId("publish-cancel");

    public ILocator Result => page.GetByTestId("publish-result");

    public ILocator Error => page.GetByTestId("publish-error");

    public ILocator Done => page.GetByTestId("publish-done");

    /// <summary>Publishes the item now: picks it (it is preselected while hidden), acknowledges a provisional scale if asked, submits, waits for the result and closes with Done.</summary>
    public async Task PublishNowAsync(Guid itemId)
    {
        await Expect(Submit).ToBeVisibleAsync();
        await ModeNow.CheckAsync();
        if (!await Pick(itemId).IsCheckedAsync())
        {
            await Pick(itemId).CheckAsync();
        }

        if (await ProvisionalAcknowledgement.CountAsync() > 0)
        {
            await ProvisionalAcknowledgement.CheckAsync();
        }

        await Submit.ClickAsync();
        await Expect(Result).ToBeVisibleAsync();
        await Done.ClickAsync();
        await Expect(Submit).ToHaveCountAsync(0);
    }
}
