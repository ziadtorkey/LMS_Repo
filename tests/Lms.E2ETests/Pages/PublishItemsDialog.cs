using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The publish dialog (M5 plan Task 27): Now, Schedule or Hide, and the acknowledgement of a provisional scale. Scaffold: update the test ids with the FE page.</summary>
public sealed class PublishItemsDialog(IPage page)
{
    public ILocator Dialog => page.GetByTestId("publish-dialog");

    public ILocator ModeNow => page.GetByTestId("publish-mode-now");

    public ILocator ModeSchedule => page.GetByTestId("publish-mode-schedule");

    public ILocator ProvisionalAcknowledgement => page.GetByTestId("publish-ack-provisional");

    public ILocator Confirm => page.GetByTestId("publish-confirm");
}
