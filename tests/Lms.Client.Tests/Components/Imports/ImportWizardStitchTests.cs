using System.Globalization;
using Bunit;
using Lms.Client.Components.Imports;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Imports;
using Lms.Shared.Organizations;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Components.Imports;

/// <summary>The import wizard in the Stitch layout (#569): numbered stepper, template panel, drop zone, review summary tiles.</summary>
public sealed class ImportWizardStitchTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly StubImportsApi _imports = new();

    public ImportWizardStitchTests()
    {
        Services.AddSingleton<IImportsApi>(_imports);
        var unit = new OrgUnitNodeDto(Guid.NewGuid(), null, Guid.NewGuid(), "مدرسة", "School", "SCH", 0, "Active", false, null, false, false, []);
        Services.AddSingleton<IUnitLookupApi>(new SingleUnit(unit));
        Culture.Current = new CultureInfo("en");
    }

    private IRenderedComponent<ImportWizard> RenderWizard()
    {
        Render<MudPopoverProvider>();
        Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
        return Render<ImportWizard>(p => p.Add(w => w.Kind, ImportKinds.Staff).Add(w => w.PollInterval, TimeSpan.FromMilliseconds(10)));
    }

    private static async Task ChooseFileAsync(IRenderedComponent<ImportWizard> cut, string name)
    {
        var upload = cut.FindComponent<InputFile>();
        await cut.InvokeAsync(() => upload.UploadFiles(InputFileContent.CreateFromBinary(new byte[8], name)));
    }

    [Fact]
    public void The_title_is_the_pages_h1()
    {
        var cut = RenderWizard();

        var h1 = Assert.Single(cut.FindAll("h1"));
        Assert.Contains("Import", h1.TextContent);
    }

    [Fact]
    public void The_steps_are_a_numbered_list_with_the_current_one_marked()
    {
        var cut = RenderWizard();

        var steps = cut.Find("ol.lms-steps[data-testid=import-steps]");
        Assert.Equal(4, steps.QuerySelectorAll("li").Length);
        Assert.Equal("step", cut.Find("[data-testid=import-step-0]").GetAttribute("aria-current"));
        Assert.Null(cut.Find("[data-testid=import-step-1]").GetAttribute("aria-current"));
        Assert.Contains("Template", cut.Find("[data-testid=import-step-0]").TextContent);
        Assert.Equal("1", cut.Find("[data-testid=import-step-0] .lms-step-number").TextContent.Trim());
    }

    [Fact]
    public async Task A_finished_step_shows_a_check_instead_of_its_number()
    {
        var cut = RenderWizard();

        await cut.WaitForElement("[data-testid=import-next]").ClickAsync(new());

        Assert.Equal("step", cut.Find("[data-testid=import-step-1]").GetAttribute("aria-current"));
        Assert.NotNull(cut.Find("[data-testid=import-step-0] .lms-step-number svg"));
    }

    [Fact]
    public void The_template_step_has_a_labelled_language_group_and_a_download_panel()
    {
        var cut = RenderWizard();

        Assert.Contains("Template language", cut.Find("[data-testid=import-template-language]").TextContent);
        var panel = cut.Find("[data-testid=import-template-panel]");
        Assert.NotNull(panel.QuerySelector("[data-testid=import-template]"));
        Assert.NotNull(panel.QuerySelector(".lms-kind-tile"));
    }

    [Fact]
    public async Task The_upload_step_is_a_drop_zone_around_the_file_input()
    {
        var cut = RenderWizard();
        await cut.WaitForElement("[data-testid=import-next]").ClickAsync(new());

        var zone = cut.Find("[data-testid=import-dropzone]");

        Assert.NotNull(zone.QuerySelector("input[type=file]"));
        Assert.Contains("Drag", zone.TextContent);
        Assert.Contains("xlsx", zone.TextContent);
    }

    [Fact]
    public async Task A_chosen_file_shows_as_a_file_row_with_its_name_left_to_right()
    {
        var cut = RenderWizard();
        await cut.WaitForElement("[data-testid=import-next]").ClickAsync(new());

        await ChooseFileAsync(cut, "staff.xlsx");

        var row = cut.WaitForElement("[data-testid=import-file-name]", Wait);
        Assert.Equal("ltr", row.GetAttribute("dir"));
        Assert.Equal("staff.xlsx", row.TextContent.Trim());
        Assert.NotNull(cut.Find(".lms-file-row"));
    }

    [Fact]
    public async Task The_review_shows_summary_tiles_for_rows_and_errors()
    {
        var cut = RenderWizard();
        await cut.WaitForElement("[data-testid=import-next]").ClickAsync(new());
        await ChooseFileAsync(cut, "staff.xlsx");
        await cut.WaitForElement("[data-testid=import-upload]:not([disabled])").ClickAsync(new());

        var summary = cut.WaitForElement("[data-testid=import-summary]", Wait);

        Assert.NotNull(summary.QuerySelector("[data-testid=import-stat-rows]"));
        Assert.NotNull(summary.QuerySelector("[data-testid=import-stat-errors]"));
        Assert.NotNull(summary.QuerySelector("[data-testid=import-stat-valid]"));
        Assert.Contains("lms-stats", summary.ClassName);
    }

    [Fact]
    public void The_wizard_renders_in_arabic_with_no_missing_keys()
    {
        Culture.Current = new CultureInfo("ar");

        var cut = RenderWizard();

        Assert.DoesNotContain("Import_", cut.Markup);
        Assert.Contains("القالب", cut.Find("[data-testid=import-step-0]").TextContent);
    }

    private sealed class SingleUnit(OrgUnitNodeDto unit) : IUnitLookupApi
    {
        public Task<IReadOnlyList<OrgUnitNodeDto>> GetUnitsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrgUnitNodeDto>>([unit]);
    }
}
