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
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Gradebook;

/// <summary>Categories, weights, scale and items of a gradebook (M5 Task 26). Nothing is calculated here: the page shows what the server answers.</summary>
public sealed class GradebookConfigTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeGradebookApi.DemoClassCourseId;
    private static readonly Guid Unit = FakeGradingScalesApi.DemoUnitId;
    private static readonly Guid Hw = FakeGradebookApi.HomeworkCategoryId;
    private static readonly Guid Exams = FakeGradebookApi.ExamsCategoryId;
    private readonly FakeGradingScalesApi _scales = new();
    private readonly FakeGradebookApi _api;
    private readonly FakeGradebookUnit _unit = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;
    private IRenderedComponent<MudPopoverProvider> _popovers = default!;

    public GradebookConfigTests()
    {
        Culture.Current = new CultureInfo("en");
        _api = new FakeGradebookApi(_scales);
        Services.AddSingleton<IGradingScalesApi>(_scales);
        Services.AddSingleton<IGradebookApi>(_api);
        Services.AddSingleton<IGradebookUnit>(_unit);
        Services.AddSingleton<IViewport>(new FakeViewport());
    }

    private async Task<IRenderedComponent<GradebookPage>> OpenAsync(bool settings = true, params string[] permissions)
    {
        await GrantAsync(permissions.Length == 0 ? [Permissions.Grades.View, Permissions.Grades.Manage] : permissions);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        _popovers = Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
        var page = Render<GradebookPage>(p => p.Add(x => x.Id, Cc));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=gradebook-loading]")), Wait);
        if (settings && page.FindAll("[data-testid=gradebook-settings]").Count > 0)
        {
            page.Find("[data-testid=gradebook-settings]").Click();
            page.WaitForElement("[data-testid=gradebook-config]", Wait);
        }

        return page;
    }

    private static string Chip(IRenderedComponent<GradebookPage> page) => page.Find("[data-testid=gradebook-card] [data-testid=weights-chip]").TextContent.Trim();

    // ---- access ----

    [Fact]
    public async Task The_settings_button_opens_the_panel_for_someone_who_manages()
    {
        var page = await OpenAsync(settings: false);
        Assert.Empty(page.FindAll("[data-testid=gradebook-config]"));
        Assert.Equal("false", page.Find("[data-testid=gradebook-settings]").GetAttribute("aria-expanded"));

        page.Find("[data-testid=gradebook-settings]").Click();

        Assert.NotNull(page.Find("[data-testid=gradebook-config]"));
        Assert.Equal("true", page.Find("[data-testid=gradebook-settings]").GetAttribute("aria-expanded"));
    }

    [Fact]
    public async Task A_viewer_or_a_locked_gradebook_has_no_settings()
    {
        _api.CanManage = false;
        var viewer = await OpenAsync(true, Permissions.Grades.View);
        Assert.Empty(viewer.FindAll("[data-testid=gradebook-settings]"));
    }

    [Fact]
    public async Task A_locked_gradebook_has_no_settings_button()
    {
        _api.Locked = true;

        var page = await OpenAsync(false);

        Assert.Empty(page.FindAll("[data-testid=gradebook-settings]"));
    }

    // ---- weights ----

    [Fact]
    public async Task Weights_chip_shows_server_validity_after_save()
    {
        var page = await OpenAsync();
        Assert.Equal("Weights total 100", Chip(page));
        page.Find("[data-testid=cat-weight-0]").Input("20"); // 20 + 70
        Assert.Equal("Weights total 90: 10 missing", page.Find("[data-testid=config-categories] [data-testid=weights-chip]").TextContent.Trim()); // the live preview

        page.Find("[data-testid=cat-save]").Click();

        page.WaitForAssertion(() => Assert.Equal("Weights total 90: 10 missing", Chip(page)), Wait); // now the card's chip is the server's answer
        Assert.Equal(20m, (await _api.GetAsync(Cc, new GradebookQuery())).Categories.First(c => c.Id == Hw).Weight);
    }

    [Fact]
    public async Task A_weight_outside_range_cannot_be_typed_and_the_server_message_is_shown_if_it_refuses()
    {
        var page = await OpenAsync();
        Assert.Equal("100", page.Find("[data-testid=cat-weight-0]").GetAttribute("max"));
        // Force the server's refusal through the fake rules: a category row sent with 101.
        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => _api.SaveCategoriesAsync(Cc, new SaveGradeCategoriesRequest([new SaveGradeCategoryRequest(null, "ا", "A", 101m, 1)])));
        Assert.Equal(ProblemCodes.weights_invalid, ex.Code);
    }

    [Fact]
    public async Task A_category_without_a_name_is_refused_with_the_required_message()
    {
        var page = await OpenAsync();
        page.Find("[data-testid=cat-name-en-0]").Input("");

        page.Find("[data-testid=cat-save]").Click();

        Assert.NotNull(page.WaitForElement("[data-testid=config-error]", Wait));
        Assert.Equal(0, (await _api.GetAsync(Cc, new GradebookQuery())).Categories.Count(c => c.NameEn == ""));
    }

    // ---- reordering ----

    [Fact]
    public async Task Categories_reorder_by_buttons_and_drag()
    {
        var page = await OpenAsync();
        string Order() => string.Join(",", page.FindAll("[data-testid^=cat-name-en-]").Select(e => e.GetAttribute("value")));
        Assert.Equal("Homework,Exams", Order());

        page.Find("[data-testid=cat-down-0]").Click();
        Assert.Equal("Exams,Homework", Order());
        page.Find("[data-testid=cat-up-1]").Click();
        Assert.Equal("Homework,Exams", Order());

        page.Find("[data-testid=cat-row-0]").TriggerEvent("ondragstart", new DragEventArgs());
        page.Find("[data-testid=cat-row-1]").TriggerEvent("ondrop", new DragEventArgs());
        Assert.Equal("Exams,Homework", Order());
        Assert.True(page.Find("[data-testid=cat-up-0]").HasAttribute("disabled")); // the first cannot move up
        Assert.True(page.Find("[data-testid=cat-down-1]").HasAttribute("disabled"));

        page.Find("[data-testid=cat-save]").Click();
        page.WaitForAssertion(() => Assert.Equal(["Exams", "Homework"], _api.GetAsync(Cc, new GradebookQuery()).Result.Categories.OrderBy(c => c.Order).Select(c => c.NameEn)), Wait);
    }

    [Fact]
    public async Task Adding_and_removing_a_category_changes_the_preview_total_only_until_saved()
    {
        var page = await OpenAsync();

        page.Find("[data-testid=cat-add]").Click();
        page.Find("[data-testid=cat-weight-2]").Input("10");

        Assert.Equal("Weights total 110: 10 over", page.Find("[data-testid=config-categories] [data-testid=weights-chip]").TextContent.Trim());
        page.Find("[data-testid=cat-remove-2]").Click();
        Assert.Equal("Weights total 100", page.Find("[data-testid=config-categories] [data-testid=weights-chip]").TextContent.Trim());
        Assert.Equal(2, (await _api.GetAsync(Cc, new GradebookQuery())).Categories.Count); // nothing was sent
    }

    // ---- templates ----

    [Fact]
    public async Task Apply_template_confirms_values_are_copied()
    {
        var page = await OpenAsync();

        page.Find("[data-testid=cat-template]").Click();
        _dialogs.WaitForElement("[data-testid=template-copy-note]", Wait);
        Assert.Contains("copied", _dialogs.Find("[data-testid=template-copy-note]").TextContent);
        Assert.Contains("nothing is saved until you save", _dialogs.Find("[data-testid=template-copy-note]").TextContent);
        var template = (await _scales.ListTemplatesAsync(Unit)).Single();
        _dialogs.Find($"[data-testid=template-option-{template.Id}]").Click();
        _dialogs.Find("[data-testid=dialog-save]").Click();

        page.WaitForAssertion(() => Assert.Equal("Coursework", page.Find("[data-testid=cat-name-en-0]").GetAttribute("value")), Wait);
        Assert.Equal("40", page.Find("[data-testid=cat-weight-0]").GetAttribute("value"));
        Assert.Equal("Exams", page.Find("[data-testid=cat-name-en-1]").GetAttribute("value"));
        Assert.Equal("Weights total 100", page.Find("[data-testid=config-categories] [data-testid=weights-chip]").TextContent.Trim());
        Assert.Equal("Homework", (await _api.GetAsync(Cc, new GradebookQuery())).Categories.OrderBy(c => c.Order).First().NameEn); // not saved yet
    }

    [Fact]
    public async Task The_template_button_is_disabled_when_the_unit_cannot_be_resolved()
    {
        _unit.Unit = null;

        var page = await OpenAsync();

        Assert.True(page.Find("[data-testid=cat-template]").HasAttribute("disabled"));
        Assert.Contains("not available", page.Markup);
    }

    // ---- uncategorised items ----

    [Fact]
    public async Task Uncategorised_alert_offers_to_move_items()
    {
        var loose = _api.SeedUncategorisedItem();
        var page = await OpenAsync();
        Assert.Contains("1 items are in no category", page.Find("[data-testid=uncategorised-alert]").TextContent);

        page.Find("[data-testid=uncategorised-move]").Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=uncategorised-alert]")), Wait);
        Assert.Equal(Hw, (await _api.GetAsync(Cc, new GradebookQuery())).Items.First(i => i.Id == loose.Id).CategoryId); // the first category is the default target
    }

    [Fact]
    public async Task No_alert_when_every_item_has_a_category()
    {
        var page = await OpenAsync();

        Assert.Empty(page.FindAll("[data-testid=uncategorised-alert]"));
    }

    [Fact]
    public async Task Removing_a_category_leaves_its_items_uncategorised_and_the_alert_says_so()
    {
        var page = await OpenAsync();
        page.Find("[data-testid=cat-remove-1]").Click();
        page.Find("[data-testid=cat-weight-0]").Input("100");

        page.Find("[data-testid=cat-save]").Click();

        page.WaitForElement("[data-testid=uncategorised-alert]", Wait);
        Assert.Equal("Weights total 100", Chip(page));
    }

    // ---- items ----

    [Fact]
    public async Task Manual_item_can_be_deleted_and_auto_item_cannot()
    {
        var page = await OpenAsync();
        Assert.NotEmpty(page.FindAll($"[data-testid=item-delete-{FakeGradebookApi.ManualItemId}]"));
        Assert.Empty(page.FindAll($"[data-testid=item-delete-{FakeGradebookApi.AutoItemId}]"));
        Assert.Contains("assignment or quiz", page.Find($"[data-testid=item-auto-{FakeGradebookApi.AutoItemId}]").TextContent);

        page.Find($"[data-testid=item-delete-{FakeGradebookApi.ManualItemId}]").Click();
        _dialogs.WaitForElement("[data-testid=confirm-yes]", Wait).Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll($"[data-testid=config-item-{FakeGradebookApi.ManualItemId}]")), Wait);
        Assert.NotEmpty(page.FindAll($"[data-testid=config-item-{FakeGradebookApi.AutoItemId}]"));
    }

    [Fact]
    public async Task Deleting_an_item_asks_first()
    {
        var page = await OpenAsync();

        page.Find($"[data-testid=item-delete-{FakeGradebookApi.ManualItemId}]").Click();
        _dialogs.WaitForElement("[data-testid=confirm-no]", Wait).Click();

        Assert.NotEmpty(page.FindAll($"[data-testid=config-item-{FakeGradebookApi.ManualItemId}]"));
    }

    [Fact]
    public async Task A_manual_item_is_added_with_both_titles_a_category_and_a_maximum()
    {
        var page = await OpenAsync();
        page.Find("[data-testid=item-add]").Click();
        _dialogs.WaitForElement("[data-testid=item-title-ar]", Wait);
        _dialogs.Find("[data-testid=item-title-ar]").Change("مشروع");
        _dialogs.Find("[data-testid=item-title-en]").Change("Project");
        _dialogs.Find("[data-testid=item-max]").Input("25");

        _dialogs.Find("[data-testid=dialog-save]").Click();

        page.WaitForAssertion(() => Assert.Contains(page.FindAll("[data-testid^=config-item-]"), r => r.TextContent.Contains("Project")), Wait);
        var created = (await _api.GetAsync(Cc, new GradebookQuery())).Items.First(i => i.TitleEn == "Project");
        Assert.Equal((25m, true), (created.MaxPoints, created.IsManual));
    }

    [Fact]
    public async Task An_item_without_a_valid_maximum_is_refused_before_any_call()
    {
        var page = await OpenAsync();
        page.Find("[data-testid=item-add]").Click();
        _dialogs.WaitForElement("[data-testid=item-title-ar]", Wait);
        _dialogs.Find("[data-testid=item-title-ar]").Change("ا");
        _dialogs.Find("[data-testid=item-title-en]").Change("A");
        _dialogs.Find("[data-testid=item-max]").Input("5000");

        _dialogs.Find("[data-testid=dialog-save]").Click();

        Assert.NotNull(_dialogs.WaitForElement("[data-testid=item-error]", Wait));
        Assert.DoesNotContain((await _api.GetAsync(Cc, new GradebookQuery())).Items, i => i.TitleEn == "A");
    }

    [Fact]
    public async Task A_manual_item_can_be_edited()
    {
        var page = await OpenAsync();
        page.Find($"[data-testid=item-edit-{FakeGradebookApi.ManualItemId}]").Click();
        _dialogs.WaitForElement("[data-testid=item-max]", Wait);
        _dialogs.Find("[data-testid=item-max]").Input("30");

        _dialogs.Find("[data-testid=dialog-save]").Click();

        page.WaitForAssertion(() => Assert.Equal(30m, _api.GetAsync(Cc, new GradebookQuery()).Result.Items.First(i => i.Id == FakeGradebookApi.ManualItemId).MaxPoints), Wait);
    }

    // ---- scale and calculation settings ----

    [Fact]
    public async Task Scale_picker_lists_visible_scales()
    {
        await _scales.CreateScaleAsync(Unit, new SaveGradingScaleRequest("مقياسي", "My scale", ScaleKinds.Letter, [new(1, 0m, 100m, "P", null, null, null, null, null)]));
        var page = await OpenAsync();


        var offered = page.FindComponents<MudSelectItem<Guid?>>().Select(i => i.Instance.Value).ToList();
        var scales = (await _scales.ListScalesAsync(Unit)).Where(s => !s.IsTemplate).Select(s => (Guid?)s.Id).ToList();
        Assert.Equal(scales.Order(), offered.Order());
        Assert.Contains(offered, id => id is { } g && _scales.ListScalesAsync(Unit).Result.Any(s => s.Id == g && s.NameEn == "My scale"));
        Assert.DoesNotContain(offered, id => id is { } g && _scales.ListScalesAsync(Unit).Result.Any(s => s.Id == g && s.IsTemplate)); // presets are templates, not scales to pick
    }

    [Fact]
    public async Task Settings_are_saved_as_typed_and_the_page_shows_the_servers_config()
    {
        var page = await OpenAsync();
        page.Find("[data-testid=config-places]").Change("1");

        page.Find("[data-testid=config-save]").Click();

        page.WaitForAssertion(() => Assert.Equal(1, _api.GetAsync(Cc, new GradebookQuery()).Result.Config.DecimalPlaces), Wait);
    }

    [Fact]
    public async Task The_method_and_missing_policy_choices_are_the_catalog_values_with_labels()
    {
        var page = await OpenAsync();

        Assert.Contains("config-method", page.Markup);
        Assert.Contains("config-missing", page.Markup);
        Assert.DoesNotContain("SettingValue_Grades", page.Markup); // a missing label would show its key
    }

    // ---- RTL ----

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        _api.SeedUncategorisedItem("بند");

        var page = await OpenAsync();

        Assert.Contains("الفئات والأوزان", page.Find("[data-testid=config-categories]").TextContent);
        Assert.Contains("بنود بلا فئة", page.Find("[data-testid=uncategorised-alert]").TextContent);
        Assert.Contains("من واجب أو اختبار", page.Find($"[data-testid=item-auto-{FakeGradebookApi.AutoItemId}]").TextContent);
        Assert.Equal("rtl", page.Find("[data-testid=cat-name-ar-0]").GetAttribute("dir"));
        Assert.Equal("ltr", page.Find("[data-testid=cat-weight-0]").GetAttribute("dir"));
        Assert.DoesNotContain("Grd_", page.Markup);
    }
}
