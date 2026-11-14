using System.Globalization;
using Bunit;
using Lms.Client.Navigation;
using Lms.Client.Pages.Grading;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Services.Grading;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Lms.Shared.Organizations;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Grading;

/// <summary>The grading scales admin page (M5 Task 23): presets and provisional scales come from the server, nothing about a band is known to the client.</summary>
public sealed class GradingScalesPageTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Unit = FakeGradingScalesApi.DemoUnitId;
    private readonly FakeGradingScalesApi _scales = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;
    private IRenderedComponent<MudPopoverProvider> _popovers = default!;
    private IRenderedComponent<MudSnackbarProvider> _snackbars = default!;

    public GradingScalesPageTests()
    {
        Culture.Current = new CultureInfo("en");
        Services.AddSingleton<IGradingScalesApi>(_scales);
        var unit = new OrgUnitNodeDto(Unit, null, Guid.NewGuid(), "مدرسة", "School", "SCH", 0, "Active", false, null, false, false, []);
        Services.AddSingleton<IUnitLookupApi>(new SingleUnit(unit));
    }

    private async Task<IRenderedComponent<GradingScalesPage>> OpenAsync(params string[] permissions)
    {
        await GrantAsync(permissions.Length == 0 ? [Permissions.Grades.View, Permissions.Grades.ConfigureScales] : permissions);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        _popovers = Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        _snackbars = Render<MudSnackbarProvider>();
        var page = Render<GradingScalesPage>();
        page.WaitForElement("[data-testid=scales-title]", Wait);
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".mud-skeleton")), Wait);
        return page;
    }

    [Fact]
    public async Task Presets_come_from_the_server()
    {
        var page = await OpenAsync();

        await page.Find("[data-testid=preset-menu] button").ClickAsync(new());
        var items = _popovers.WaitForElements("[data-testid^=preset-sa-]", Wait);

        Assert.Equal(["preset-sa-letter", "preset-sa-percentage"], items.Select(i => i.GetAttribute("data-testid")!).Order(StringComparer.Ordinal));
        Assert.Empty(page.FindAll("[data-testid^=scale-sa-]")); // a template is not listed as a scale of the unit
    }

    [Fact]
    public async Task Choosing_a_preset_adds_a_provisional_copy_and_confirm_clears_the_chip()
    {
        var page = await OpenAsync();
        await page.Find("[data-testid=preset-menu] button").ClickAsync(new());
        await _popovers.WaitForElement("[data-testid=preset-sa-letter]", Wait).ClickAsync(new());

        var chip = page.WaitForElement("[data-testid^=scale-provisional-]", Wait);
        Assert.Contains("Provisional", chip.TextContent);

        page.Find("[data-testid^=scale-confirm-]").Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid^=scale-provisional-]")), Wait);
        Assert.Empty(page.FindAll("[data-testid^=scale-confirm-]"));
    }

    [Fact]
    public async Task Update_is_offered_for_unedited_copies_only_and_clears_after_it()
    {
        var copy = await _scales.CopyPresetAsync(Unit, new CopyScalePresetRequest("sa-percentage"));
        _scales.MarkUpdateAvailable("sa-percentage");
        var page = await OpenAsync();

        Assert.NotEmpty(page.FindAll($"[data-testid=scale-update-{copy.Id}]"));
        await page.Find($"[data-testid=scale-update-action-{copy.Id}]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Empty(page.FindAll($"[data-testid=scale-update-{copy.Id}]")), Wait);
    }

    [Fact]
    public async Task An_edited_copy_is_not_offered_an_update()
    {
        var copy = await _scales.CopyPresetAsync(Unit, new CopyScalePresetRequest("sa-percentage"));
        await _scales.UpdateScaleAsync(Unit, copy.Id, new SaveGradingScaleRequest("ن", "N", ScaleKinds.Percentage, [new(1, 0m, 100m, "X", null, null, null, null, null)]));
        _scales.MarkUpdateAvailable("sa-percentage");

        var page = await OpenAsync();

        Assert.Empty(page.FindAll("[data-testid^=scale-update-]")); // the edit detached the copy from its preset
    }

    [Fact]
    public async Task Make_default_asks_for_confirmation_first()
    {
        var copy = await _scales.CopyPresetAsync(Unit, new CopyScalePresetRequest("sa-percentage"));
        var page = await OpenAsync();

        page.Find($"[data-testid=scale-makedefault-{copy.Id}]").Click();
        _dialogs.WaitForElement("[data-testid=confirm-no]", Wait);
        Assert.Empty(page.FindAll($"[data-testid=scale-default-{copy.Id}]")); // nothing changed yet
        _dialogs.Find("[data-testid=confirm-yes]").Click();

        page.WaitForElement($"[data-testid=scale-default-{copy.Id}]", Wait);
    }

    [Fact]
    public async Task Deleting_a_scale_in_use_shows_the_plain_message()
    {
        var copy = await _scales.CopyPresetAsync(Unit, new CopyScalePresetRequest("sa-percentage"));
        _scales.MarkInUse(copy.Id);
        var page = await OpenAsync();
        Assert.NotEmpty(page.FindAll($"[data-testid=scale-inuse-{copy.Id}]"));

        page.Find($"[data-testid=scale-delete-{copy.Id}]").Click();
        _dialogs.WaitForElement("[data-testid=confirm-yes]", Wait).Click();

        _snackbars.WaitForAssertion(() => Assert.Contains("gradebook uses", _snackbars.Markup, StringComparison.OrdinalIgnoreCase), Wait);
        Assert.NotEmpty(page.FindAll($"[data-testid=scale-{copy.Id}]")); // still there
    }

    [Fact]
    public async Task A_viewer_without_ConfigureScales_sees_the_scales_but_no_admin_actions()
    {
        await _scales.CopyPresetAsync(Unit, new CopyScalePresetRequest("sa-percentage"));
        var page = await OpenAsync(Permissions.Grades.View);

        Assert.NotEmpty(page.FindAll("[data-testid^=scale-edit-]")); // the view action
        Assert.Empty(page.FindAll("[data-testid=scale-new]"));
        Assert.Empty(page.FindAll("[data-testid=preset-menu]"));
        Assert.Empty(page.FindAll("[data-testid^=scale-delete-]"));
        Assert.Empty(page.FindAll("[data-testid^=scale-confirm-]"));
    }

    [Fact]
    public async Task Without_Grades_View_the_page_is_forbidden()
    {
        var page = await OpenAsync(Permissions.Forums.View);

        Assert.NotNull(page.Find("[data-testid=scales-forbidden]"));
    }

    [Fact]
    public void The_nav_entry_needs_ConfigureScales_so_a_teacher_does_not_see_it()
    {
        var entry = NavCatalog.Items.Single(i => i.Href == "grading/scales");

        Assert.Equal(Permissions.Grades.ConfigureScales, entry.RequiredPermission);
    }

    [Fact]
    public async Task The_empty_state_points_to_the_next_step()
    {
        var page = await OpenAsync();

        Assert.NotNull(page.Find("[data-testid=scales-empty]"));
    }

    [Fact]
    public async Task The_page_renders_in_arabic_with_localized_labels()
    {
        Culture.Current = new CultureInfo("ar");
        await _scales.CopyPresetAsync(Unit, new CopyScalePresetRequest("sa-percentage"));
        var page = await OpenAsync();

        Assert.Contains("مقاييس التقدير", page.Find("[data-testid=scales-title]").TextContent);
        Assert.Contains("مؤقت", page.Markup);
        Assert.DoesNotContain("Scales_", page.Markup); // a missing key would show its name
        Assert.DoesNotContain("Grd_", page.Markup);
    }

    private sealed class SingleUnit(OrgUnitNodeDto unit) : IUnitLookupApi
    {
        public Task<IReadOnlyList<OrgUnitNodeDto>> GetUnitsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrgUnitNodeDto>>([unit]);
    }
}
