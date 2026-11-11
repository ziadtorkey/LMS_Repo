using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The templates list (/templates) and its apply dialog (FE Task 28, #207).</summary>
public sealed class TemplatesPage(IPage page)
{
    public async Task OpenAsync()
    {
        await page.GotoAsync("/templates");
        await page.GetByTestId("templates-title").WaitForAsync(); // a full page load takes about 11 s (#275)
    }

    /// <summary>The list row whose shown name is exactly <paramref name="name"/>.</summary>
    public ILocator Row(string name) => page.Locator("tbody tr").Filter(new LocatorFilterOptions
    {
        Has = page.Locator("td").Filter(new LocatorFilterOptions { HasTextRegex = new Regex("^[ ]*" + Regex.Escape(name) + "[ ]*$") }),
    });

    /// <summary>Applies the template under the target unit (picked by its shown name) with the placeholder values; returns the result text.</summary>
    public async Task<string> ApplyAsync(string templateName, string targetName, IReadOnlyDictionary<string, string> values)
    {
        await Row(templateName).Locator("[data-testid^='template-apply-']").ClickAsync();
        await page.GetByTestId("apply-target").ClickAsync();
        await page.Locator(".mud-popover-open .mud-list-item")
            .Filter(new LocatorFilterOptions { HasTextRegex = new Regex("^[ ]*" + Regex.Escape(targetName) + "[ ]*$") })
            .First.ClickAsync();
        foreach (var (key, value) in values)
        {
            await page.Locator($"input[data-testid='placeholder-{key}'], [data-testid='placeholder-{key}'] input").First.FillAsync(value);
        }

        await page.GetByTestId("dialog-save").ClickAsync();
        var result = page.GetByTestId("apply-result");
        await result.WaitForAsync();
        var text = await result.InnerTextAsync();
        await page.GetByTestId("dialog-close").ClickAsync();
        await page.GetByTestId("dialog-close").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        return text;
    }
}
