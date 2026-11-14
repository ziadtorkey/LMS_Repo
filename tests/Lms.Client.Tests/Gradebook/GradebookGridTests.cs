using System.Globalization;
using Bunit;
using Lms.Client.Pages.Gradebook;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Services.Grading;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Gradebook;

/// <summary>A viewport the test sets: phone or desktop, ready at once.</summary>
public sealed class FakeViewport : IViewport
{
    public bool Ready { get; set; } = true;
    public bool IsPhone { get; set; }
    public event Action? Changed;
    public Task EnsureAsync() => Task.CompletedTask;
    public void Raise() => Changed?.Invoke();
}

/// <summary>The gradebook grid, read and render (M5 Task 24). Every number comes from the response; the client never calculates a grade (L18).</summary>
public sealed class GradebookGridTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeGradebookApi.DemoClassCourseId;
    private static readonly Guid S0 = FakeGradebookApi.StudentIds[0];
    private static readonly Guid S1 = FakeGradebookApi.StudentIds[1];
    private readonly FakeGradebookApi _api = new();
    private readonly FakeViewport _viewport = new();

    public GradebookGridTests()
    {
        Culture.Current = new CultureInfo("en");
        Services.AddSingleton<IGradebookApi>(_api);
        Services.AddSingleton<IViewport>(_viewport);
    }

    private async Task<IRenderedComponent<GradebookPage>> OpenAsync(params string[] permissions)
    {
        await GrantAsync(permissions.Length == 0 ? [Permissions.Grades.View, Permissions.Grades.Manage] : permissions);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        Render<MudPopoverProvider>();
        Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
        var page = Render<GradebookPage>(p => p.Add(x => x.Id, Cc));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=gradebook-loading]")), Wait);
        return page;
    }

    private static string Text(AngleSharp.Dom.IElement e) => e.TextContent.Trim();

    [Fact]
    public async Task Final_comes_from_the_response()
    {
        // A final that no arithmetic on the cells could produce: the page must show it as given.
        _api.Transform = (book, _) => book with { Rows = book.Rows with { Items = [.. book.Rows.Items.Select(r => r with { FinalPercent = 12.3m, BandCode = "X", BandLabelEn = "Server band" })] } };

        var page = await OpenAsync();

        Assert.Equal("12.30", Text(page.Find($"[data-testid=final-{S0}]")));
        Assert.Contains("Server band", Text(page.Find($"[data-testid=band-{S0}]")));
    }

    [Fact]
    public async Task Category_results_come_from_the_response_too()
    {
        _api.Transform = (book, _) => book with { Rows = book.Rows with { Items = [.. book.Rows.Items.Select(r => r with
        {
            CategoryResults = [.. r.CategoryResults.Select(c => c with { Percent = 77.7m })],
        })] } };

        var page = await OpenAsync();

        Assert.Equal("77.70", Text(page.Find($"[data-testid=catres-{S0}-{FakeGradebookApi.HomeworkCategoryId}]")));
    }

    [Fact]
    public async Task Cell_states_have_text_alternatives()
    {
        _api.SetCell(S0, new GradeCellDto(FakeGradebookApi.ManualItemId, 17m, GradeCellStates.Override, "Re-marked"));
        _api.SetCell(S1, new GradeCellDto(FakeGradebookApi.ManualItemId, 5m, GradeCellStates.NewerScoreExists, null));
        _api.SetCell(FakeGradebookApi.StudentIds[2], new GradeCellDto(FakeGradebookApi.ManualItemId, null, GradeCellStates.Excused, null));
        var page = await OpenAsync();

        string Label(Guid student, Guid item) => page.Find($"[data-testid=cell-{student}-{item}]").GetAttribute("aria-label")!;

        Assert.Equal("9 of 10, from Assignment 1", Label(S0, FakeGradebookApi.AutoItemId));            // from an event
        Assert.Equal("17 of 20, overridden: Re-marked", Label(S0, FakeGradebookApi.ManualItemId));      // overridden, with its reason
        Assert.Equal("5 of 20, overridden; a newer score exists", Label(S1, FakeGradebookApi.ManualItemId));
        Assert.Equal("Excused", Label(FakeGradebookApi.StudentIds[2], FakeGradebookApi.ManualItemId));
        Assert.Equal("No score", Label(FakeGradebookApi.StudentIds[2], FakeGradebookApi.AutoItemId));
        Assert.Contains("Newer score", page.Find($"[data-testid=cell-{S1}-{FakeGradebookApi.ManualItemId}]").TextContent);
        Assert.Contains("Excused", page.Find($"[data-testid=cell-{FakeGradebookApi.StudentIds[2]}-{FakeGradebookApi.ManualItemId}]").TextContent);
    }

    [Fact]
    public async Task A_manual_score_reads_plainly()
    {
        var page = await OpenAsync();

        Assert.Equal("18 of 20", page.Find($"[data-testid=cell-{S0}-{FakeGradebookApi.ManualItemId}]").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task Grid_uses_role_grid_with_colgroup_headers()
    {
        var page = await OpenAsync();

        var grid = page.Find("[data-testid=gradebook-grid]");
        Assert.Equal("grid", grid.GetAttribute("role"));
        Assert.Equal("gb-hint", grid.GetAttribute("aria-describedby")); // the key hint under the grid
        // One category header per category, spanning its items and its result column.
        var groups = page.FindAll("th[scope=colgroup]");
        Assert.Equal(2, groups.Count);
        Assert.Equal("2", page.Find($"[data-testid=category-{FakeGradebookApi.HomeworkCategoryId}]").GetAttribute("colspan"));
        Assert.Equal(2, page.FindAll("colgroup[span]").Count(c => c.GetAttribute("span") == "2") - 1); // two category groups plus the Final / Band pair
        // Every row starts with a row header, and each item is a column header.
        Assert.Equal(3, page.FindAll("tbody th[scope=row]").Count);
        Assert.Equal(2, page.FindAll("thead th[scope=col][data-testid^=item-]").Count);
        // Roving tabindex: exactly one cell is a tab stop.
        Assert.Single(page.FindAll("td[role=gridcell][tabindex='0']"));
    }

    [Fact]
    public async Task Pages_students_by_50()
    {
        _api.Transform = (book, q) => book with { Rows = new PagedResult<GradeRowDto>(book.Rows.Items, 120, q.Page, q.PageSize) };

        var page = await OpenAsync();

        Assert.Equal(GradingLimits.GridPageSize, _api.LastQuery!.PageSize);
        Assert.Equal(1, _api.LastQuery.Page);
        Assert.NotNull(page.Find("[data-testid=gradebook-paging]"));
        Assert.Equal(3, page.FindAll(".mud-pagination-item").Count(i => int.TryParse(i.TextContent.Trim(), out _)));   // 120 students: pages 1, 2, 3
        page.FindAll("[data-testid=gradebook-paging] button").First(b => b.TextContent.Trim() == "2").Click();

        page.WaitForAssertion(() => Assert.Equal(2, _api.LastQuery!.Page), Wait);
        Assert.Equal(GradingLimits.GridPageSize, _api.LastQuery!.PageSize);
    }

    [Fact]
    public async Task A_short_roster_shows_no_pager()
    {
        var page = await OpenAsync();

        Assert.Empty(page.FindAll("[data-testid=gradebook-paging]"));
    }

    [Fact]
    public async Task Locked_gradebook_is_read_only_with_a_banner()
    {
        _api.Locked = true;

        var page = await OpenAsync();

        Assert.NotNull(page.Find("[data-testid=gradebook-locked]"));
        Assert.NotEmpty(page.FindAll("[data-testid=gradebook-grid]")); // still readable
        Assert.Empty(page.FindAll("td input")); // nothing is editable in this task, and a locked book never is
    }

    [Fact]
    public async Task Empty_states_for_no_students_and_no_items()
    {
        _api.NoStudents = true;
        var noStudents = await OpenAsync();
        Assert.NotNull(noStudents.Find("[data-testid=gradebook-no-students]"));
        Assert.NotNull(noStudents.Find("[data-testid=gradebook-enrollment-link]"));
        Assert.Empty(noStudents.FindAll("[data-testid=gradebook-grid]"));
    }

    [Fact]
    public async Task Empty_state_for_no_items()
    {
        _api.NoItems = true;

        var page = await OpenAsync();

        Assert.NotNull(page.Find("[data-testid=gradebook-no-items]"));
        Assert.Contains("Assignments and quizzes appear here", page.Find("[data-testid=gradebook-no-items]").TextContent);
    }

    [Fact]
    public async Task Auto_item_header_says_hidden_until_published()
    {
        var page = await OpenAsync();

        Assert.Equal("Hidden until published", Text(page.Find($"[data-testid=item-state-{FakeGradebookApi.AutoItemId}]")));
        Assert.Equal("Hidden", Text(page.Find($"[data-testid=item-state-{FakeGradebookApi.ManualItemId}]"))); // a manual item is just hidden
        Assert.Contains("out of 10", page.Find($"[data-testid=item-{FakeGradebookApi.AutoItemId}]").TextContent);
    }

    [Fact]
    public async Task A_published_item_says_published_and_a_scheduled_one_its_date()
    {
        await _api.PublishAsync(Cc, new PublishItemsRequest([FakeGradebookApi.ManualItemId], PublishModes.Now, null, true));
        await _api.PublishAsync(Cc, new PublishItemsRequest([FakeGradebookApi.AutoItemId], PublishModes.Schedule, DateTime.UtcNow.AddDays(3), true));

        var page = await OpenAsync();

        Assert.Equal("Published", Text(page.Find($"[data-testid=item-state-{FakeGradebookApi.ManualItemId}]")));
        Assert.StartsWith("Scheduled ", Text(page.Find($"[data-testid=item-state-{FakeGradebookApi.AutoItemId}]")));
    }

    [Fact]
    public async Task Only_one_layout_is_rendered_below_600px()
    {
        _viewport.IsPhone = true;

        var phone = await OpenAsync();

        Assert.NotEmpty(phone.FindAll("[data-testid=student-first-list]"));
        Assert.Empty(phone.FindAll("[data-testid=gradebook-grid]")); // not hidden with CSS: absent
        Assert.Empty(phone.FindAll("table"));
    }

    [Fact]
    public async Task The_desktop_layout_has_no_phone_list()
    {
        var desktop = await OpenAsync();

        Assert.NotEmpty(desktop.FindAll("[data-testid=gradebook-grid]"));
        Assert.Empty(desktop.FindAll("[data-testid=student-first-list]"));
    }

    [Fact]
    public async Task Resizing_to_a_phone_swaps_the_layout()
    {
        var page = await OpenAsync();
        Assert.NotEmpty(page.FindAll("[data-testid=gradebook-grid]"));

        _viewport.IsPhone = true;
        await page.InvokeAsync(() => _viewport.Raise());

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=student-first-list]")), Wait);
        Assert.Empty(page.FindAll("[data-testid=gradebook-grid]"));
    }

    [Fact]
    public async Task Student_drawer_preview_uses_the_staff_read()
    {
        await _api.PublishAsync(Cc, new PublishItemsRequest([FakeGradebookApi.ManualItemId], PublishModes.Now, null, true));
        var page = await OpenAsync();

        page.Find($"[data-testid=student-{S0}]").Click();

        page.WaitForElement("[data-testid=drawer-items]", Wait);
        Assert.Equal(1, _api.StudentViewCalls);
        Assert.Contains("Sara Ahmed", page.Find("[data-testid=drawer-title]").TextContent);
        Assert.Single(page.FindAll("[data-testid^=drawer-item-]")); // only the published item: the hidden one is not there at all, not even as a count
        Assert.Contains("18", page.Find($"[data-testid=drawer-item-{FakeGradebookApi.ManualItemId}]").TextContent);
        Assert.NotNull(page.Find("[data-testid=drawer-final]"));
        Assert.Contains("published items only", page.Find("[data-testid=drawer-note]").TextContent);
    }

    [Fact]
    public async Task The_drawer_says_so_when_nothing_is_published_yet()
    {
        var page = await OpenAsync();

        page.Find($"[data-testid=student-{S0}]").Click();

        page.WaitForElement("[data-testid=drawer-empty]", Wait);
        Assert.Empty(page.FindAll("[data-testid=drawer-items]"));
    }

    [Fact]
    public async Task The_weights_chip_is_a_success_at_100_and_says_how_far_off_otherwise()
    {
        var ok = await OpenAsync();
        Assert.Equal("Weights total 100", Text(ok.Find("[data-testid=weights-chip]")));
    }

    [Fact]
    public async Task The_weights_chip_warns_when_the_total_is_off()
    {
        _api.Transform = (book, _) => book with { WeightsTotal = 90m, IsValid = false };

        var page = await OpenAsync();

        Assert.Equal("Weights total 90: 10 missing", Text(page.Find("[data-testid=weights-chip]")));
    }

    [Fact]
    public async Task The_header_subtitle_names_the_term_and_the_scale()
    {
        var page = await OpenAsync();

        Assert.Contains("Term 1", page.Markup);
        Assert.Contains("Term 1 · ", page.Markup);
        Assert.Equal("Term 1", Text(page.Find("[data-testid=term-chip]")));
    }

    [Fact]
    public async Task A_load_failure_offers_try_again_and_recovers()
    {
        _api.NextFailure = new ApiProblemException(500, null, null, null);

        var page = await OpenAsync();
        Assert.NotNull(page.Find("[data-testid=gradebook-error]"));
        Assert.Empty(page.FindAll("[data-testid=gradebook-grid]"));
        page.Find("[data-testid=gradebook-retry]").Click();

        page.WaitForElement("[data-testid=gradebook-grid]", Wait);
        Assert.Empty(page.FindAll("[data-testid=gradebook-error]"));
    }

    [Fact]
    public async Task Without_Grades_View_or_Manage_the_page_is_forbidden()
    {
        var page = await OpenAsync(Permissions.Forums.View);

        Assert.NotNull(page.Find("[data-testid=gradebook-forbidden]"));
        Assert.Empty(page.FindAll("[data-testid=gradebook-grid]"));
    }

    [Fact]
    public async Task The_gradebook_tab_is_marked_current()
    {
        var page = await OpenAsync();

        Assert.Equal("tab-gradebook", Assert.Single(page.FindAll("[aria-current=page]")).GetAttribute("data-testid"));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");

        var page = await OpenAsync();

        Assert.Contains("دفتر الدرجات", page.Find("[data-testid=gradebook-title]").TextContent);
        Assert.Contains("مجموع الأوزان", page.Find("[data-testid=weights-chip]").TextContent);
        Assert.Contains("مخفي حتى النشر", page.Find($"[data-testid=item-state-{FakeGradebookApi.AutoItemId}]").TextContent);
        Assert.Equal("9 من 10، من الواجب الأول", page.Find($"[data-testid=cell-{S0}-{FakeGradebookApi.AutoItemId}]").GetAttribute("aria-label"));
        Assert.DoesNotContain("Grd_", page.Markup); // a missing key would show its name
        // Numbers stay left to right inside the Arabic page.
        Assert.Equal("ltr", page.Find($"[data-testid=final-{S0}]").GetAttribute("dir"));
    }
}
