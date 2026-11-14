using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M5;

/// <summary>
/// The keyboard and screen-reader pass of the gradebook grid (plan #621 Task 42; L24; QA3's review of #628): arrows, Enter, Tab, Esc, Shift+F10, and the
/// focused cell never under the sticky header or the student column (a bounding-box assertion after every key, in both reading directions), the
/// outline visible in forced-colors mode, and the student button reachable by Enter. Skipped until the grid pages and the a11y sweep (#660, #663, #710)
/// are on main; the flows are written against FE1's id map and have not run.
/// </summary>
public sealed class GradebookKeyboardTests(E2EFixture fixture) : M5TestBase(fixture)
{
    // The focused cell against the sticky header rows and the student column, in the page's reading direction.
    private const string Geometry = """
        () => {
            const cell = document.activeElement;
            const grid = document.querySelector('[data-testid=gradebook-grid]');
            if (!cell || !grid || !grid.contains(cell)) return { inside: false };
            const c = cell.getBoundingClientRect();
            const headerBottom = Math.max(...[...grid.querySelectorAll('thead th')].map(t => t.getBoundingClientRect().bottom));
            const first = grid.querySelector('tbody th, tbody td');
            const f = first.getBoundingClientRect();
            const rtl = getComputedStyle(grid).direction === 'rtl';
            return { inside: true, belowHeader: c.top >= headerBottom - 1, clearOfColumn: rtl ? c.right <= f.left + 1 : c.left >= f.right - 1, rtl };
        }
        """;

    [Theory(Skip = NeedsGradebookGrid + " and the a11y sweep (#710)")]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Grid_is_operable_by_keyboard_only(string culture)
    {
        var (world, admin, _) = await StartWorldAsync(culture);
        var items = new List<Guid>();
        for (var i = 1; i <= 6; i++)
        {
            items.Add(await M5Kit.CreateManualItemAsync(admin, world.ClassCourseId, $"بند {i}", $"Item {i}", 10));
        }

        await using var teacher = await OpenTeacherAsync(culture);
        var page = teacher.Page;
        var gradebook = new GradebookPage(page);
        await gradebook.OpenAsync(world.ClassCourseId);
        await AssertDirectionAsync(page, culture);
        await Expect(gradebook.Grid).ToBeVisibleAsync();
        Assert.Equal("nav", await gradebook.Grid.GetAttributeAsync("data-mode"));

        // Arrows, Home, End and Ctrl+Home move the one tab stop; after every key the focused cell is not under the sticky edges.
        await gradebook.Cell(world.Student.Id, items[0]).FocusAsync();
        foreach (var key in new[] { "ArrowRight", "ArrowRight", "ArrowDown", "ArrowLeft", "ArrowUp", "End", "Home", "Control+Home" })
        {
            await page.Keyboard.PressAsync(key);
            var box = await page.EvaluateAsync<System.Text.Json.JsonElement>(Geometry);
            Assert.True(box.GetProperty("inside").GetBoolean(), $"focus left the grid after {key}");
            Assert.True(box.GetProperty("belowHeader").GetBoolean(), $"the focused cell is under the sticky header after {key}");
            Assert.True(box.GetProperty("clearOfColumn").GetBoolean(), $"the focused cell is under the student column after {key}");
        }

        // Enter edits, Esc cancels, Enter then a value then Enter saves and stays; the hint and the live regions announce it.
        var cell = gradebook.Cell(world.Student.Id, items[0]);
        await cell.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.Equal("edit", await gradebook.Grid.GetAttributeAsync("data-mode"));
        await page.Keyboard.PressAsync("Escape");
        Assert.Equal("nav", await gradebook.Grid.GetAttributeAsync("data-mode"));
        await gradebook.TypeScoreAsync(world.Student.Id, items[0], "7");
        await Expect(gradebook.LivePolite).Not.ToBeEmptyAsync();

        // A value the server refuses is announced once, assertively (above the maximum of 10).
        await cell.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await gradebook.CellInput(world.Student.Id, items[0]).FillAsync("99");
        await page.Keyboard.PressAsync("Enter");
        await Expect(gradebook.LiveAssertive).Not.ToBeEmptyAsync();
        await page.Keyboard.PressAsync("Escape");

        // Shift+F10 opens the cell menu; Esc closes it and returns the focus to the cell.
        await cell.FocusAsync();
        await page.Keyboard.PressAsync("Shift+F10");
        await Expect(page.GetByTestId("cell-menu")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.GetByTestId("cell-menu")).ToHaveCountAsync(0);

        // Tab in navigation mode leaves the grid.
        await cell.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.False(await page.EvaluateAsync<bool>("() => document.querySelector('[data-testid=gradebook-grid]').contains(document.activeElement)"));

        // The student's name is a button inside the grid: Enter must open the preview (the grid's key handler leaves it alone).
        await gradebook.StudentButton(world.Student.Id).FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.GetByTestId("student-drawer")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");

        // Forced colors: the focus outline is still drawn.
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ForcedColors = ForcedColors.Active });
        await cell.FocusAsync();
        var outline = await page.EvaluateAsync<string>("() => { const s = getComputedStyle(document.activeElement); return s.outlineStyle + ' ' + s.outlineWidth; }");
        Assert.DoesNotContain("none", outline);
        Assert.DoesNotContain(" 0px", outline);
    }
}
