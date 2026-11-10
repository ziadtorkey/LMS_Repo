using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The context chooser (/choose-context) shown after login when the user holds several assignments.</summary>
public sealed class ChooseContextPage(IPage page)
{
    public ILocator Options => page.Locator("[data-testid^='context-option-']");
    public ILocator NoContexts => page.GetByTestId("no-contexts");

    public Task WaitAsync() => page.WaitForURLAsync(url => url.Contains("/choose-context", StringComparison.Ordinal));

    /// <summary>Picks the option whose text contains every given fragment (e.g. unit name and role label).</summary>
    public async Task ChooseAsync(params string[] fragments)
    {
        var option = Options;
        foreach (var fragment in fragments)
        {
            option = option.Filter(new LocatorFilterOptions { HasText = fragment });
        }

        await option.First.ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/choose-context", StringComparison.Ordinal));
    }
}
