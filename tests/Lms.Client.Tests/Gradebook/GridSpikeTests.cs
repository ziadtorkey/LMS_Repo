using System.Diagnostics;
using System.Globalization;
using Bunit;
using Lms.Client.Components.Gradebook;
using Lms.Client.Tests.Support;
using Lms.Shared.Grading;
using Xunit.Abstractions;

namespace Lms.Client.Tests.Gradebook;

/// <summary>
/// Spike S1 (M5 plan Task 24): what a full class costs to render, and whether one changed row re-renders alone. The result is written up in
/// <c>docs/spikes/2026-11-18-m5-grid.md</c>; these tests keep it true (a regression here means the per-row guard broke).
/// </summary>
public sealed class GridSpikeTests : ClientTestContext
{
    private const int Students = 60;
    private const int ItemsPerCategory = 5;
    private const int Categories = 5; // 25 items in all

    private readonly ITestOutputHelper _output;

    public GridSpikeTests(ITestOutputHelper output)
    {
        _output = output;
        Culture.Current = new CultureInfo("en");
    }

    private static (IReadOnlyList<CategoryGroup> Groups, List<GradeRowDto> Rows) Build()
    {
        var categories = Enumerable.Range(1, Categories).Select(c => new GradeCategoryDto(Guid.NewGuid(), $"فئة {c}", $"Category {c}", 20m, c, false)).ToList();
        var items = categories.SelectMany((c, ci) => Enumerable.Range(1, ItemsPerCategory).Select(i =>
            new GradeItemDto(Guid.NewGuid(), c.Id, $"بند {ci}-{i}", $"Item {ci}-{i}", i % 2 == 0 ? "Assignment" : null, null, i % 2 != 0, 10m, PublishStates.Hidden, null, null, ci * 10 + i))).ToList();
        var groups = CategoryGroup.From(categories, items);
        var rows = Enumerable.Range(1, Students).Select(s => new GradeRowDto(Guid.NewGuid(), $"طالب {s}", $"Student {s}", $"S-{s:D4}",
            [.. items.Select(i => new GradeCellDto(i.Id, 7m, i.IsManual ? GradeCellStates.Manual : GradeCellStates.Event, null))],
            [.. categories.Select(c => new GradeCategoryResultDto(c.Id, 70m, 14m))], 70m, "B", "جيد", "Good", null, 1)).ToList();
        return (groups, rows);
    }

    [Fact]
    public void A_full_class_of_60_students_and_25_items_renders_in_one_pass()
    {
        var (groups, rows) = Build();
        var watch = Stopwatch.StartNew();

        var grid = Render<GradebookGrid>(p => p.Add(x => x.Groups, groups).Add(x => x.Rows, rows));
        watch.Stop();

        _output.WriteLine($"S1 initial render of {Students} x {Categories * ItemsPerCategory}: {watch.ElapsedMilliseconds} ms (bUnit, no browser)");
        Assert.Equal(Students, grid.FindAll("tbody tr").Count);
        Assert.Equal(Students * (Categories * ItemsPerCategory + Categories + 2), grid.FindAll("td[role=gridcell]").Count); // every column is a gridcell: items, category results, final, band
        Assert.True(watch.ElapsedMilliseconds < 5000, "a full class must render in seconds, not minutes");
    }

    [Fact]
    public void Changing_one_row_re_renders_that_row_only()
    {
        var (groups, rows) = Build();
        var grid = Render<GradebookGrid>(p => p.Add(x => x.Groups, groups).Add(x => x.Rows, rows));
        var before = grid.FindComponents<GradeRow>().ToDictionary(r => r.Instance.Row.StudentId, r => r.RenderCount);

        var edited = rows[10] with { FinalPercent = 99m, Version = 2 };
        var next = new List<GradeRowDto>(rows) { [10] = edited };
        var watch = Stopwatch.StartNew();
        grid.Render(p => p.Add(x => x.Groups, groups).Add(x => x.Rows, next));
        watch.Stop();

        var rerendered = grid.FindComponents<GradeRow>().Where(r => r.RenderCount != before[r.Instance.Row.StudentId]).Select(r => r.Instance.Row.StudentId).ToList();
        _output.WriteLine($"S1 one changed row of {Students}: {watch.ElapsedMilliseconds} ms, {rerendered.Count} row component(s) re-rendered");
        Assert.Equal([edited.StudentId], rerendered);
        Assert.Equal("99.00", grid.Find($"[data-testid=final-{edited.StudentId}]").TextContent.Trim());
    }

    [Fact]
    public void Rows_are_keyed_so_a_reordered_page_moves_rows_instead_of_rebuilding_them()
    {
        var (groups, rows) = Build();
        var grid = Render<GradebookGrid>(p => p.Add(x => x.Groups, groups).Add(x => x.Rows, rows));
        var counts = grid.FindComponents<GradeRow>().ToDictionary(r => r.Instance.Row.StudentId, r => r.RenderCount);

        grid.Render(p => p.Add(x => x.Groups, groups).Add(x => x.Rows, [.. rows.AsEnumerable().Reverse()]));

        Assert.Equal($"grade-row-{rows[^1].StudentId}", grid.FindAll("tbody tr")[0].GetAttribute("data-testid"));
        // The key map works on row positions, so every row whose position changed renders once more; the keyed rows are moved, not rebuilt.
        Assert.All(grid.FindComponents<GradeRow>(), r => Assert.Equal(counts[r.Instance.Row.StudentId] + 1, r.RenderCount));
    }

    [Fact]
    public void Only_the_first_cell_is_a_tab_stop_whatever_the_size()
    {
        var (groups, rows) = Build();

        var grid = Render<GradebookGrid>(p => p.Add(x => x.Groups, groups).Add(x => x.Rows, rows));

        Assert.Single(grid.FindAll("td[role=gridcell][tabindex='0']"));
        Assert.Equal(Students * (Categories * ItemsPerCategory + Categories + 2) - 1, grid.FindAll("td[role=gridcell][tabindex='-1']").Count);
    }
}
