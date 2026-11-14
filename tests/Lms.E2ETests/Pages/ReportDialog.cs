using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The report-a-message dialog and the thread controls that open it (FE1 #761, M6 Task 24).</summary>
public sealed class ReportDialog(IPage page)
{
    public ILocator Dialog => page.GetByTestId("report");
    public ILocator Error => page.GetByTestId("report-error");
    public ILocator Reason(string reason) => page.GetByTestId($"report-reason-{reason}");

    /// <summary>The flag button of a message in the open thread.</summary>
    public ILocator FlagButton(Guid messageId) => page.GetByTestId($"msg-report-{messageId}");

    /// <summary>The "reported" chip that replaces the flag once the caller has reported the message.</summary>
    public ILocator ReportedChip(Guid messageId) => page.GetByTestId($"msg-reported-{messageId}");

    public async Task ReportAsync(Guid messageId, string reason, string? note = null)
    {
        await FlagButton(messageId).ClickAsync();
        await Dialog.WaitForAsync();
        await Reason(reason).ClickAsync();
        if (note is not null)
        {
            await page.Locator("[data-testid='report-note'] textarea, [data-testid='report-note'] input").Locator("visible=true").First.FillAsync(note);
        }

        await page.GetByTestId("report-submit").ClickAsync();
        await Dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }
}
