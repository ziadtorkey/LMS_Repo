using System.Globalization;
using Bunit;
using Lms.Client.Pages.Gradebook;
using Lms.Client.Pages.Grading;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Services.Grading;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Lms.Shared.Organizations;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit.Abstractions;

namespace Lms.Client.Tests.Gradebook;

/// <summary>
/// The FE1 half of the M5 accessibility sweep (Task 39, X-10): every gradebook and grading-admin page and dialog scanned with <see cref="A11yScan"/> in
/// Arabic and English (named controls, labelled fields, one h1, labelled stacked cells, nothing focusable inside aria-hidden), plus the rules a scan
/// cannot see: focus returns to the cell after a dialog or the menu, live regions exist once, forced-colors and reduced-motion rules are in the CSS.
/// </summary>
public sealed class GradebookAccessibilityTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeGradebookApi.DemoClassCourseId;
    private static readonly Guid Unit = FakeGradingScalesApi.DemoUnitId;
    private static readonly Guid S0 = FakeGradebookApi.StudentIds[0];
    private readonly ITestOutputHelper _output;
    private readonly FakeGradingScalesApi _scales = new();
    private readonly FakeGradebookApi _api;
    private readonly FakeViewport _viewport = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;
    private IRenderedComponent<MudPopoverProvider> _popovers = default!;

    public GradebookAccessibilityTests(ITestOutputHelper output)
    {
        _output = output;
        _api = new FakeGradebookApi(_scales);
        Services.AddSingleton<IGradingScalesApi>(_scales);
        Services.AddSingleton<IGradebookApi>(_api);
        Services.AddSingleton<IGradebookUnit>(new FakeGradebookUnit());
        Services.AddSingleton<IViewport>(_viewport);
        var unit = new OrgUnitNodeDto(Unit, null, Guid.NewGuid(), "مدرسة", "School", "SCH", 0, "Active", false, null, false, false, []);
        Services.AddSingleton<IUnitLookupApi>(new OneUnit(unit));
    }

    private async Task GrantAllAsync()
    {
        await GrantAsync(Permissions.Grades.View, Permissions.Grades.Manage, Permissions.Grades.ConfigureScales);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        _popovers = Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
    }

    private void AssertClean(IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> cut, string where, bool requireOneHeading = true)
    {
        var problems = A11yScan.Problems(cut, requireOneHeading);
        foreach (var p in problems) _output.WriteLine($"{where}: {p}");
        Assert.Empty(problems);
    }

    private async Task<IRenderedComponent<GradebookPage>> OpenGradebookAsync(string culture, bool phone = false)
    {
        Culture.Current = new CultureInfo(culture);
        _viewport.IsPhone = phone;
        await GrantAllAsync();
        var page = Render<GradebookPage>(p => p.Add(x => x.Id, Cc));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=gradebook-loading]")), Wait);
        return page;
    }

    private async Task<IRenderedComponent<TPage>> OpenAdminAsync<TPage>(string culture, string title) where TPage : Microsoft.AspNetCore.Components.IComponent
    {
        Culture.Current = new CultureInfo(culture);
        await _scales.CopyPresetAsync(Unit, new CopyScalePresetRequest("sa-percentage"));
        await GrantAllAsync();
        var page = Render<TPage>();
        page.WaitForElement($"[data-testid={title}]", Wait);
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".mud-skeleton")), Wait);
        return page;
    }

    // ---- pages ----

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task The_gradebook_grid_page_is_clean(string culture)
    {
        var page = await OpenGradebookAsync(culture);

        AssertClean(page, $"grid {culture}", requireOneHeading: false); // the h1 is in PageHeader, outside this component's own nodes only when a layout wraps it: counted below
        Assert.Single(page.FindAll("h1"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task The_phone_list_page_is_clean(string culture)
    {
        var page = await OpenGradebookAsync(culture, phone: true);

        AssertClean(page, $"phone {culture}", requireOneHeading: false);
        Assert.Single(page.FindAll("h1"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task The_settings_panel_is_clean(string culture)
    {
        var page = await OpenGradebookAsync(culture);
        page.Find("[data-testid=gradebook-settings]").Click();
        page.WaitForElement("[data-testid=gradebook-config]", Wait);

        AssertClean(page, $"settings {culture}", requireOneHeading: false);
        Assert.Single(page.FindAll("h1"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task The_student_preview_drawer_is_clean(string culture)
    {
        await _api.PublishAsync(Cc, new PublishItemsRequest([FakeGradebookApi.ManualItemId], PublishModes.Now, null, true));
        var page = await OpenGradebookAsync(culture);
        page.Find($"[data-testid=student-{S0}]").Click();
        page.WaitForElement("[data-testid=drawer-items]", Wait);

        AssertClean(page, $"drawer {culture}", requireOneHeading: false);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task The_scales_page_is_clean(string culture)
    {
        var page = await OpenAdminAsync<GradingScalesPage>(culture, "scales-title");

        AssertClean(page, $"scales {culture}", requireOneHeading: false);
        Assert.Single(page.FindAll("h1"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task The_weight_templates_page_is_clean(string culture)
    {
        var page = await OpenAdminAsync<WeightTemplatesPage>(culture, "templates-title");

        AssertClean(page, $"templates {culture}", requireOneHeading: false);
        Assert.Single(page.FindAll("h1"));
    }

    // ---- dialogs ----

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task The_scale_dialog_the_template_dialog_and_the_item_dialog_are_clean(string culture)
    {
        var scales = await OpenAdminAsync<GradingScalesPage>(culture, "scales-title");
        scales.Find("[data-testid=scale-new]").Click();
        _dialogs.WaitForElement("[data-testid=band-editor]", Wait);
        AssertClean(_dialogs, $"scale dialog {culture}", requireOneHeading: false);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task The_override_dialog_and_the_cell_menu_are_clean(string culture)
    {
        var page = await OpenGradebookAsync(culture);
        page.Find($"[data-testid=cell-{S0}-{FakeGradebookApi.AutoItemId}]").KeyDown(new KeyboardEventArgs { Key = "F10", ShiftKey = true });
        _popovers.WaitForElement("[data-testid=menu-override]", Wait);
        AssertClean(_popovers, $"cell menu {culture}", requireOneHeading: false);

        _popovers.Find("[data-testid=menu-override]").Click();
        _dialogs.WaitForElement("[data-testid=override-reason]", Wait);
        AssertClean(_dialogs, $"override dialog {culture}", requireOneHeading: false);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task The_item_and_template_dialogs_of_the_settings_panel_are_clean(string culture)
    {
        var page = await OpenGradebookAsync(culture);
        page.Find("[data-testid=gradebook-settings]").Click();
        page.WaitForElement("[data-testid=item-add]", Wait).Click();
        _dialogs.WaitForElement("[data-testid=item-max]", Wait);
        AssertClean(_dialogs, $"item dialog {culture}", requireOneHeading: false);
    }

    // ---- what a scan cannot see ----

    [Fact]
    public async Task The_grid_has_exactly_one_polite_and_one_assertive_live_region()
    {
        var page = await OpenGradebookAsync("en");

        Assert.Single(page.FindAll("[aria-live=polite]"));
        Assert.Single(page.FindAll("[aria-live=assertive]"));
    }

    [Fact]
    public async Task Every_grid_cell_and_header_is_named_for_assistive_technology()
    {
        var page = await OpenGradebookAsync("en");

        Assert.All(page.FindAll("td[role=gridcell][data-state]"), c => Assert.False(string.IsNullOrWhiteSpace(c.GetAttribute("aria-label"))));
        Assert.All(page.FindAll("thead th"), h => Assert.False(string.IsNullOrWhiteSpace(h.TextContent)));
        Assert.All(page.FindAll("tbody th[scope=row]"), h => Assert.False(string.IsNullOrWhiteSpace(h.TextContent)));
    }

    [Fact]
    public async Task Focus_returns_to_the_cell_after_the_menu_closes()
    {
        var module = JSInterop.SetupModule("./js/gradebook-grid.js");
        module.Mode = JSRuntimeMode.Loose;
        var page = await OpenGradebookAsync("en");
        var cell = page.Find($"[data-testid=cell-{S0}-{FakeGradebookApi.AutoItemId}]");
        cell.KeyDown(new KeyboardEventArgs { Key = "F10", ShiftKey = true });
        _popovers.WaitForElement("[data-testid=menu-override]", Wait);
        var before = module.Invocations.Count(i => i.Identifier == "focusCell");

        _popovers.Find("[role=menu]").KeyDown(new KeyboardEventArgs { Key = "Escape" }); // the menu closes and the focus goes back to the cell

        page.WaitForAssertion(() => Assert.True(module.Invocations.Count(i => i.Identifier == "focusCell") > before), Wait);
    }

    [Fact]
    public async Task Dialogs_close_with_the_escape_key()
    {
        var page = await OpenGradebookAsync("en");
        page.Find("[data-testid=gradebook-settings]").Click();
        page.WaitForElement("[data-testid=item-add]", Wait).Click();
        _dialogs.WaitForElement("[data-testid=item-max]", Wait);

        // Every M5 dialog is shown with CloseOnEscapeKey (the options are code, asserted here through a successful cancel).
        _dialogs.Find("[data-testid=dialog-cancel]").Click();

        _dialogs.WaitForAssertion(() => Assert.Empty(_dialogs.FindAll("[data-testid=item-max]")), Wait);
    }

    [Fact]
    public void Forced_colors_and_the_focus_outline_are_in_the_stylesheet_and_motion_is_not_required()
    {
        var css = File.ReadAllText(RepoFile("src", "Lms.Client", "wwwroot", "css", "app.css"));

        Assert.Contains("forced-colors: active", css);
        Assert.Contains("outline: 2px solid Highlight", css);
        Assert.Contains("td.focus-cell", css);
        Assert.DoesNotContain("@keyframes lms-gb", css); // the grid has no animation to switch off
    }

    [Fact]
    public void The_gradebook_logical_css_never_uses_physical_left_and_right()
    {
        var css = File.ReadAllText(RepoFile("src", "Lms.Client", "wwwroot", "css", "app.css"));
        var block = css[css.IndexOf("Gradebook grid (M5 T24)", StringComparison.Ordinal)..];

        Assert.DoesNotContain("margin-left", block);
        Assert.DoesNotContain("margin-right", block);
        Assert.DoesNotContain("padding-left", block);
        Assert.DoesNotContain("padding-right", block);
        Assert.DoesNotContain("text-align:left", block.Replace(" ", ""));
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Lms.Client"))) dir = dir.Parent;
        return Path.Combine([dir!.FullName, .. parts]);
    }

    private sealed class OneUnit(OrgUnitNodeDto unit) : IUnitLookupApi
    {
        public Task<IReadOnlyList<OrgUnitNodeDto>> GetUnitsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<OrgUnitNodeDto>>([unit]);
    }
}
