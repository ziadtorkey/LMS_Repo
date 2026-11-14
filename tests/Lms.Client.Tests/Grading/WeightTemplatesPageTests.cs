using System.Globalization;
using Bunit;
using Lms.Client.Components.Grading;
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

/// <summary>The weight templates admin page and its editor (M5 Task 23).</summary>
public sealed class WeightTemplatesPageTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Unit = FakeGradingScalesApi.DemoUnitId;
    private readonly FakeGradingScalesApi _scales = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public WeightTemplatesPageTests()
    {
        Culture.Current = new CultureInfo("en");
        Services.AddSingleton<IGradingScalesApi>(_scales);
        var unit = new OrgUnitNodeDto(Unit, null, Guid.NewGuid(), "مدرسة", "School", "SCH", 0, "Active", false, null, false, false, []);
        Services.AddSingleton<IUnitLookupApi>(new SingleUnit(unit));
    }

    private async Task<IRenderedComponent<WeightTemplatesPage>> OpenAsync(params string[] permissions)
    {
        await GrantAsync(permissions.Length == 0 ? [Permissions.Grades.View, Permissions.Grades.ConfigureScales] : permissions);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
        var page = Render<WeightTemplatesPage>();
        page.WaitForElement("[data-testid=templates-title]", Wait);
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".mud-skeleton")), Wait);
        return page;
    }

    [Fact]
    public async Task The_page_lists_templates_with_their_total_chip()
    {
        var page = await OpenAsync();

        var template = (await _scales.ListTemplatesAsync(Unit)).Single();
        Assert.Contains("100", page.Find($"[data-testid=template-total-{template.Id}]").TextContent);
        Assert.Contains("Standard split", page.Find($"[data-testid=template-{template.Id}]").TextContent);
    }

    [Fact]
    public void Weights_chip_previews_the_typed_total()
    {
        var items = new List<CategoryWeightsEditor.WeightRow>
        {
            new() { NameAr = "أ", NameEn = "A", Weight = 30m },
            new() { NameAr = "ب", NameEn = "B", Weight = 50m },
        };
        var editor = Render<CategoryWeightsEditor>(p => p.Add(e => e.Items, items));
        Assert.Contains("80", editor.Find("[data-testid=weights-total]").TextContent);
        Assert.NotNull(editor.Find("[data-testid=weights-hint]"));

        editor.Find("[data-testid=weight-value-1]").Input("70");

        Assert.Contains("100", editor.Find("[data-testid=weights-total]").TextContent);
        Assert.Empty(editor.FindAll("[data-testid=weights-hint]"));
    }

    [Fact]
    public async Task Adding_and_removing_categories_changes_the_total()
    {
        var items = new List<CategoryWeightsEditor.WeightRow> { new() { NameAr = "أ", NameEn = "A", Weight = 40m } };
        var editor = Render<CategoryWeightsEditor>(p => p.Add(e => e.Items, items));

        await editor.Find("[data-testid=weight-add]").ClickAsync(new());
        editor.Find("[data-testid=weight-value-1]").Input("25");
        Assert.Contains("65", editor.Find("[data-testid=weights-total]").TextContent);
        await editor.Find("[data-testid=weight-remove-1]").ClickAsync(new());

        Assert.Contains("40", editor.Find("[data-testid=weights-total]").TextContent);
    }

    [Fact]
    public async Task A_new_template_is_saved_through_the_api()
    {
        var page = await OpenAsync();
        page.Find("[data-testid=template-new]").Click();
        _dialogs.WaitForElement("[data-testid=weights-editor]", Wait);
        _dialogs.Find("[data-testid=template-name-ar]").Change("قالب");
        _dialogs.Find("[data-testid=template-name-en]").Change("Mine");
        _dialogs.Find("[data-testid=weight-name-ar-0]").Change("اختبار");
        _dialogs.Find("[data-testid=weight-name-en-0]").Change("Exam");
        _dialogs.Find("[data-testid=weight-value-0]").Input("100");

        _dialogs.Find("[data-testid=dialog-save]").Click();

        page.WaitForAssertion(() => Assert.Contains("Mine", page.Markup), Wait);
        Assert.Contains(await _scales.ListTemplatesAsync(Unit), t => t.NameEn == "Mine" && t.WeightsTotal == 100m);
    }

    [Fact]
    public async Task A_template_with_an_unnamed_category_shows_the_required_message()
    {
        var page = await OpenAsync();
        page.Find("[data-testid=template-new]").Click();
        _dialogs.WaitForElement("[data-testid=weights-editor]", Wait);
        _dialogs.Find("[data-testid=template-name-ar]").Change("قالب");
        _dialogs.Find("[data-testid=template-name-en]").Change("Bad");

        _dialogs.Find("[data-testid=dialog-save]").Click();

        _dialogs.WaitForElement("[data-testid=template-error]", Wait);
        Assert.DoesNotContain(await _scales.ListTemplatesAsync(Unit), t => t.NameEn == "Bad");
    }

    [Fact]
    public void The_weight_field_stops_at_the_range_the_server_accepts()
    {
        var items = new List<CategoryWeightsEditor.WeightRow> { new() { NameAr = "أ", NameEn = "A", Weight = 10m } };
        var editor = Render<CategoryWeightsEditor>(p => p.Add(e => e.Items, items));

        var field = editor.Find("[data-testid=weight-value-0]");

        Assert.Equal("0", field.GetAttribute("min"));
        Assert.Equal("100", field.GetAttribute("max"));
    }

    [Fact]
    public async Task Deleting_a_template_asks_first()
    {
        var page = await OpenAsync();
        var template = (await _scales.ListTemplatesAsync(Unit)).Single();

        page.Find($"[data-testid=template-delete-{template.Id}]").Click();
        _dialogs.WaitForElement("[data-testid=confirm-yes]", Wait).Click();

        page.WaitForElement("[data-testid=templates-empty]", Wait);
        Assert.Empty(await _scales.ListTemplatesAsync(Unit));
    }

    [Fact]
    public async Task A_viewer_without_ConfigureScales_cannot_create_or_delete()
    {
        var page = await OpenAsync(Permissions.Grades.View);

        Assert.Empty(page.FindAll("[data-testid=template-new]"));
        Assert.Empty(page.FindAll("[data-testid^=template-delete-]"));
        Assert.NotEmpty(page.FindAll("[data-testid^=template-edit-]")); // view only
    }

    [Fact]
    public async Task The_page_renders_in_arabic_with_localized_labels()
    {
        Culture.Current = new CultureInfo("ar");
        var page = await OpenAsync();

        Assert.Contains("قوالب الأوزان", page.Find("[data-testid=templates-title]").TextContent);
        Assert.DoesNotContain("WTemplates_", page.Markup);
        Assert.DoesNotContain("Grd_Tab", page.Markup);
    }

    private sealed class SingleUnit(OrgUnitNodeDto unit) : IUnitLookupApi
    {
        public Task<IReadOnlyList<OrgUnitNodeDto>> GetUnitsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrgUnitNodeDto>>([unit]);
    }
}
