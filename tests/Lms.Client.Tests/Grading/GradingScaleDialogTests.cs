using System.Globalization;
using Bunit;
using Lms.Client.Components.Grading;
using Lms.Client.Pages.Grading;
using Lms.Client.Services.Grading;
using Lms.Client.Tests.Support;
using Lms.Shared.Grading;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Grading;

/// <summary>The scale dialog, its band editor and the coverage strip (M5 Task 23).</summary>
public sealed class GradingScaleDialogTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Unit = FakeGradingScalesApi.DemoUnitId;
    private readonly FakeGradingScalesApi _scales = new();

    public GradingScaleDialogTests()
    {
        Culture.Current = new CultureInfo("en");
        Services.AddSingleton<IGradingScalesApi>(_scales);
    }

    private async Task<IRenderedComponent<MudDialogProvider>> ShowAsync(GradingScaleDto? existing = null, bool readOnly = false)
    {
        Render<MudPopoverProvider>();
        var provider = Render<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<GradingScaleDialog>("scale", new DialogParameters<GradingScaleDialog>
        {
            { x => x.UnitId, Unit }, { x => x.Existing, existing }, { x => x.ReadOnly, readOnly },
        }));
        provider.WaitForElement("[data-testid=band-editor]", Wait);
        return provider;
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(0, 50, 1)]    // ends below 100
    [InlineData(10, 100, 1)]  // starts above 0
    public void Coverage_of_a_single_band_reports_the_ends(int min, int max, int issues)
    {
        Assert.Equal(issues, BandCoverage.Analyse([(min, max)]).Count);
    }

    [Fact]
    public void Coverage_reports_a_gap_an_overlap_and_accepts_touching_or_one_hundredth_steps()
    {
        Assert.Equal(CoverageIssueKind.Gap, Assert.Single(BandCoverage.Analyse([(0m, 40m), (50m, 100m)])).Kind);
        Assert.Equal(CoverageIssueKind.Overlap, Assert.Single(BandCoverage.Analyse([(0m, 60m), (50m, 100m)])).Kind);
        Assert.Empty(BandCoverage.Analyse([(0m, 49.99m), (50m, 100m)]));
        Assert.Empty(BandCoverage.Analyse([(0m, 50m), (50m, 100m)]));
        Assert.Equal(CoverageIssueKind.Inverted, BandCoverage.Analyse([(0m, 100m), (70m, 60m)]).First(i => i.Kind == CoverageIssueKind.Inverted).Kind);
    }

    [Fact]
    public void Coverage_strip_shows_gaps_and_overlaps_as_text()
    {
        var bands = new List<BandRow>
        {
            new() { Code = "A", MinPercent = 50m, MaxPercent = 100m },
            new() { Code = "F", MinPercent = 0m, MaxPercent = 40m },
        };

        var strip = Render<BandCoverageStrip>(p => p.Add(s => s.Bands, bands));

        Assert.Contains("40", strip.Find("[data-testid=band-issue-Gap]").TextContent);
        Assert.Contains("50", strip.Find("[data-testid=band-issue-Gap]").TextContent);
        Assert.Empty(strip.FindAll("[data-testid=band-coverage-ok]"));
    }

    [Fact]
    public void Coverage_strip_says_so_when_the_bands_are_fine()
    {
        var bands = new List<BandRow> { new() { Code = "A", MinPercent = 0m, MaxPercent = 100m } };

        var strip = Render<BandCoverageStrip>(p => p.Add(s => s.Bands, bands));

        Assert.NotNull(strip.Find("[data-testid=band-coverage-ok]"));
        Assert.Empty(strip.FindAll("[data-testid=band-coverage-issues]"));
    }

    [Fact]
    public async Task A_new_scale_starts_with_one_band_over_the_whole_range()
    {
        var dialog = await ShowAsync();

        Assert.Single(dialog.FindAll("[data-testid^=band-code-]"));
        Assert.NotNull(dialog.Find("[data-testid=band-coverage-ok]"));
    }

    [Fact]
    public async Task Adding_a_band_updates_the_coverage_text()
    {
        var dialog = await ShowAsync();

        await dialog.Find("[data-testid=band-add]").ClickAsync(new());
        Assert.Equal(2, dialog.FindAll("[data-testid^=band-code-]").Count);
        Assert.Empty(dialog.FindAll("[data-testid=band-coverage-issues]")); // a band without a range says nothing yet
        dialog.Find("[data-testid=band-min-1]").Input("50");
        dialog.Find("[data-testid=band-max-1]").Input("60");

        Assert.NotNull(dialog.WaitForElement("[data-testid=band-issue-Overlap]", Wait));
    }

    [Fact]
    public async Task Saving_without_names_shows_the_required_message()
    {
        var dialog = await ShowAsync();

        await dialog.Find("[data-testid=dialog-save]").ClickAsync(new());

        Assert.NotNull(dialog.Find("[data-testid=scale-error]"));
    }

    [Fact]
    public async Task A_valid_scale_is_saved_through_the_api()
    {
        var dialog = await ShowAsync();
        dialog.Find("[data-testid=scale-name-ar]").Change("مقياس");
        dialog.Find("[data-testid=scale-name-en]").Change("My scale");
        dialog.Find("[data-testid=band-code-0]").Input("P");

        await dialog.Find("[data-testid=dialog-save]").ClickAsync(new());

        dialog.WaitForAssertion(async () =>
        {
            var mine = (await _scales.ListScalesAsync(Unit)).Where(s => !s.IsTemplate).ToList();
            Assert.Single(mine);
            Assert.Equal("My scale", mine[0].NameEn);
            Assert.Equal("P", Assert.Single(mine[0].Bands).Code);
        }, Wait);
    }

    [Fact]
    public async Task The_server_refusing_the_bands_is_shown_in_plain_words()
    {
        var dialog = await ShowAsync();
        dialog.Find("[data-testid=scale-name-ar]").Change("مقياس");
        dialog.Find("[data-testid=scale-name-en]").Change("Bad");
        dialog.Find("[data-testid=band-code-0]").Input("P");
        dialog.Find("[data-testid=band-max-0]").Input("60");

        await dialog.Find("[data-testid=dialog-save]").ClickAsync(new());

        var error = dialog.WaitForElement("[data-testid=scale-error]", Wait);
        Assert.DoesNotContain("scale_bands_invalid", error.TextContent);
        Assert.False(string.IsNullOrWhiteSpace(error.TextContent));
    }

    [Fact]
    public async Task A_preset_is_shown_read_only_with_its_bands_from_the_data()
    {
        var preset = (await _scales.ListScalesAsync(Unit)).First(s => s.IsTemplate && s.SeedKey == "sa-letter");

        var dialog = await ShowAsync(preset, readOnly: true);

        Assert.NotNull(dialog.Find("[data-testid=scale-readonly]"));
        Assert.Equal(preset.Bands.Count, dialog.FindAll("[data-testid^=band-code-]").Count);
        Assert.Empty(dialog.FindAll("[data-testid=dialog-save]"));
        Assert.Empty(dialog.FindAll("[data-testid=band-add]"));
        Assert.Empty(dialog.FindAll("[data-testid^=band-remove-]"));
    }

    [Fact]
    public async Task Removing_a_band_is_possible_only_when_more_than_one_remains()
    {
        var dialog = await ShowAsync();
        Assert.Empty(dialog.FindAll("[data-testid^=band-remove-]"));

        await dialog.Find("[data-testid=band-add]").ClickAsync(new());
        await dialog.Find("[data-testid=band-remove-1]").ClickAsync(new());

        Assert.Single(dialog.FindAll("[data-testid^=band-code-]"));
    }
}
