using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The course list (/courses) and its create dialog (FE Task 29, #264).</summary>
public sealed class CoursesPage(IPage page)
{
    public sealed record NewCourse(
        string Code, string NameAr, string NameEn, string OwnerUnitName, int WeeklyPeriods = 0,
        IReadOnlyList<string>? AllocateTo = null);

    public ILocator Title => page.GetByTestId("course-title");
    public ILocator DialogError => page.GetByTestId("dialog-error");

    /// <summary>The inline error under the code field (a taken code is shown there, not in the dialog alert).</summary>
    public ILocator CodeError => page.Locator(".mud-input-control").Filter(new LocatorFilterOptions { Has = page.GetByTestId("course-code") })
        .Locator(".mud-input-helper-text");

    public async Task OpenAsync()
    {
        await page.GotoAsync("/courses");
        await Title.WaitForAsync(); // a full page load takes about 11 s (#275): WaitFor, not Expect
    }

    /// <summary>The list row whose code is exactly <paramref name="code"/> (codes are unique in a client, D38).</summary>
    public ILocator Row(string code) => page.Locator("tbody tr").Filter(new LocatorFilterOptions
    {
        Has = page.Locator("td span[dir='ltr']").Filter(new LocatorFilterOptions { HasTextRegex = new Regex("^\\s*" + Regex.Escape(code) + "\\s*$") }),
    });

    /// <summary>
    /// The number in the Allocations column of the row of <paramref name="code"/> (the column is found by its header text, so it
    /// does not matter where it sits in the table).
    /// </summary>
    public async Task<int> AllocationCountAsync(string code, string headerText)
    {
        var headers = await page.Locator("thead th").AllTextContentsAsync();
        var column = headers.ToList().FindIndex(h => h.Trim() == headerText);
        Assert.True(column >= 0, $"No '{headerText}' column among: {string.Join(" | ", headers)}");
        var cell = Row(code).Locator("td").Nth(column);
        return int.Parse((await cell.InnerTextAsync()).Trim());
    }

    public async Task SearchAsync(string text)
    {
        var search = Field("course-search");
        await search.FillAsync(text);
        await search.PressAsync("Enter");
    }

    /// <summary>Fills the create dialog and saves; with a refused save the dialog stays open (see <see cref="DialogError"/>).</summary>
    public async Task FillAndSaveAsync(NewCourse course)
    {
        await page.GetByTestId("course-new").ClickAsync();
        await Field("course-code").FillAsync(course.Code);
        await Field("course-name-ar").FillAsync(course.NameAr);
        await Field("course-name-en").FillAsync(course.NameEn);
        await Field("course-periods").FillAsync(course.WeeklyPeriods.ToString());
        await Pickers.PickAsync(page, "course-owner", course.OwnerUnitName);
        foreach (var unit in course.AllocateTo ?? [])
        {
            await Pickers.PickInMultiAsync(page, "course-allocate", unit);
        }

        await page.GetByTestId("dialog-save").ClickAsync();
    }

    /// <summary>Creates the course and waits for the dialog to close.</summary>
    public async Task CreateAsync(NewCourse course)
    {
        await FillAndSaveAsync(course);
        await page.GetByTestId("dialog-save").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        await page.Mouse.MoveAsync(0, 0);
    }

    private ILocator Field(string testId) =>
        page.Locator($"input[data-testid='{testId}'], [data-testid='{testId}'] input, [data-testid='{testId}'] textarea").First;
}
