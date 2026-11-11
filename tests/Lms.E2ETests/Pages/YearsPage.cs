using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.Pages;

/// <summary>Academic years (/academic-years?unit={id}): years of a unit with their terms and calendar.</summary>
public sealed partial class YearsPage(IPage page)
{
    public ILocator Inherited => page.GetByTestId("years-inherited");

    public async Task GotoAsync(Guid unitId)
    {
        await page.GotoAsync($"/academic-years?unit={unitId}");
        await page.GetByTestId("unit-picker").WaitForAsync();
        await Expect(page.Locator(".mud-progress-linear")).ToHaveCountAsync(0);
    }

    /// <summary>The card of the year named <paramref name="name"/> (in the page language).</summary>
    public ILocator Year(string name) =>
        page.Locator("[data-testid^='year-']").Filter(new LocatorFilterOptions { Has = page.Locator("h6").GetByText(name, new LocatorGetByTextOptions { Exact = true }) });

    /// <summary>Activates a Planned year (its button disappears once it is Active).</summary>
    public async Task ActivateAsync(Guid yearId)
    {
        await page.GetByTestId($"year-activate-{yearId}").ClickAsync();
        await Expect(page.GetByTestId($"year-status-{yearId}")).ToHaveAttributeAsync("data-status", "Active");
    }

    public async Task<Guid> YearIdAsync(string name)
    {
        var ids = await Year(name).EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('data-testid'))");
        var id = ids.Select(t => YearCard().Match(t)).Single(m => m.Success).Groups[1].Value;
        return Guid.Parse(id);
    }

    /// <summary>Defines a year here: "New year", or "Define own years" when the unit currently inherits its parent's.</summary>
    public async Task<Guid> DefineYearAsync(string nameAr, string nameEn, DateOnly start, DateOnly end, string shownName)
    {
        var own = page.GetByTestId("years-define-own");
        await (await own.CountAsync() > 0 ? own : page.GetByTestId("year-new")).ClickAsync();
        await page.GetByTestId("name-ar").FillAsync(nameAr);
        await page.GetByTestId("name-en").FillAsync(nameEn);
        await MudControls.PickDateAsync(page, DateInput("year-start"), start);
        await MudControls.ClosePickersAsync(page);
        await MudControls.PickDateAsync(page, DateInput("year-end"), end);
        await MudControls.ClosePickersAsync(page);
        await page.GetByTestId("year-save").ClickAsync();
        await Expect(page.Locator(".mud-dialog")).ToHaveCountAsync(0);
        await Year(shownName).WaitForAsync();
        return await YearIdAsync(shownName);
    }

    public async Task AddTermAsync(Guid yearId, string nameAr, string nameEn, DateOnly start, DateOnly end)
    {
        await page.GetByTestId($"term-new-{yearId}").ClickAsync();
        await page.GetByTestId("name-ar").FillAsync(nameAr);
        await page.GetByTestId("name-en").FillAsync(nameEn);
        await MudControls.PickDateAsync(page, DateInput("term-start"), start);
        await MudControls.ClosePickersAsync(page);
        await MudControls.PickDateAsync(page, DateInput("term-end"), end);
        await MudControls.ClosePickersAsync(page);
        await page.GetByTestId("term-save").ClickAsync();
        await Expect(page.Locator(".mud-dialog")).ToHaveCountAsync(0);
    }

    public async Task AddEventAsync(Guid yearId, string titleAr, string titleEn, string kindText, DateOnly start, DateOnly end)
    {
        await page.GetByTestId($"event-new-{yearId}").ClickAsync();
        await page.GetByTestId("event-title-ar").FillAsync(titleAr);
        await page.GetByTestId("event-title-en").FillAsync(titleEn);
        await MudControls.SelectAsync(page, page.GetByTestId("event-kind"), kindText);
        await MudControls.PickDateAsync(page, DateInput("event-start"), start);
        await MudControls.ClosePickersAsync(page);
        await MudControls.PickDateAsync(page, DateInput("event-end"), end);
        await MudControls.ClosePickersAsync(page);
        await page.GetByTestId("event-save").ClickAsync();
        await Expect(page.Locator(".mud-dialog")).ToHaveCountAsync(0);
    }

    /// <summary>The input of a MudDatePicker (while open, the picker copies the test id onto its popover too).</summary>
    private ILocator DateInput(string testId) => page.Locator($"input[data-testid='{testId}']");

    public ILocator Terms(Guid yearId) => page.GetByTestId($"terms-{yearId}");

    public ILocator Calendar(Guid yearId) => page.GetByTestId($"calendar-{yearId}");

    [GeneratedRegex("^year-([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$")]
    private static partial Regex YearCard();
}
