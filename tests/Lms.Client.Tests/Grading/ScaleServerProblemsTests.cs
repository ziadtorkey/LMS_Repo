using System.Globalization;
using System.Net;
using System.Text;
using Bunit;
using Lms.Client.Pages.Grading;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Services.Grading;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Lms.Shared.Organizations;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Grading;

/// <summary>
/// The server's <c>scale_bands_invalid</c> answer carries <c>problems[{kind, from, to}]</c> (BE #641): each gap or overlap is shown as text next to the
/// bands it concerns, not only as a generic alert (QA of #647), and the read-only reason names where a scale comes from.
/// </summary>
public sealed class ScaleServerProblemsTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Unit = FakeGradingScalesApi.DemoUnitId;
    private static readonly Guid Head = Guid.Parse("00000000-0000-0000-0000-0000000e00aa");
    private readonly FakeGradingScalesApi _scales = new();

    public ScaleServerProblemsTests()
    {
        Culture.Current = new CultureInfo("en");
        Services.AddSingleton<IGradingScalesApi>(_scales);
    }

    private async Task<IRenderedComponent<MudDialogProvider>> ShowAsync(GradingScaleDto? existing = null, bool readOnly = false, string? source = null)
    {
        Render<MudPopoverProvider>();
        var provider = Render<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        await provider.InvokeAsync(() => dialogs.ShowAsync<GradingScaleDialog>("scale", new DialogParameters<GradingScaleDialog>
        {
            { x => x.UnitId, Unit }, { x => x.Existing, existing }, { x => x.ReadOnly, readOnly }, { x => x.SourceUnitName, source },
        }));
        provider.WaitForElement("[data-testid=band-editor]", Wait);
        return provider;
    }

    /// <summary>Two bands the client hint is happy with (they follow each other by 0.01), named and ready to save.</summary>
    private async Task<IRenderedComponent<MudDialogProvider>> TwoGoodBandsAsync()
    {
        var dialog = await ShowAsync();
        dialog.Find("[data-testid=scale-name-ar]").Change("مقياس");
        dialog.Find("[data-testid=scale-name-en]").Change("Two bands");
        dialog.Find("[data-testid=band-code-0]").Input("B");
        dialog.Find("[data-testid=band-max-0]").Input("49.99");
        await dialog.Find("[data-testid=band-add]").ClickAsync(new());
        dialog.Find("[data-testid=band-code-1]").Input("A");
        dialog.Find("[data-testid=band-min-1]").Input("50");
        dialog.Find("[data-testid=band-max-1]").Input("100");
        dialog.Find("[data-testid=band-min-0]").Input("0");
        Assert.NotNull(dialog.Find("[data-testid=band-coverage-ok]")); // the client hint sees nothing wrong
        return dialog;
    }

    [Fact]
    public async Task A_gap_the_server_found_and_the_hint_did_not_is_listed_and_marks_the_bands_around_it()
    {
        var dialog = await TwoGoodBandsAsync();
        _scales.NextBandProblems = [new ApiProblemItem("Gap", 49.99m, 50m)];

        await dialog.Find("[data-testid=dialog-save]").ClickAsync(new());

        var issue = dialog.WaitForElement("[data-testid=band-server-Gap]", Wait);
        Assert.Contains("49.99", issue.TextContent);
        Assert.Contains("50", issue.TextContent);
        Assert.NotEmpty(dialog.FindAll("[data-testid=band-problem-0]")); // the band that ends where the gap starts
        Assert.NotEmpty(dialog.FindAll("[data-testid=band-problem-1]")); // and the one that starts where it ends
        Assert.Equal("true", dialog.Find("[data-testid=band-0]").GetAttribute("aria-invalid"));
    }

    [Fact]
    public async Task Each_problem_is_a_line_of_its_own()
    {
        var dialog = await TwoGoodBandsAsync();
        _scales.NextBandProblems = [new ApiProblemItem("Gap", 20m, 30m), new ApiProblemItem("Overlap", 60m, 70m)];

        await dialog.Find("[data-testid=dialog-save]").ClickAsync(new());

        dialog.WaitForElement("[data-testid=band-server-issues]", Wait);
        Assert.Single(dialog.FindAll("[data-testid=band-server-Gap]"));
        Assert.Single(dialog.FindAll("[data-testid=band-server-Overlap]"));
        Assert.Contains("overlap", dialog.Find("[data-testid=band-server-Overlap]").TextContent);
    }

    [Fact]
    public async Task An_overlap_marks_the_bands_that_share_the_range()
    {
        var dialog = await TwoGoodBandsAsync();
        _scales.NextBandProblems = [new ApiProblemItem("Overlap", 40m, 60m)];

        await dialog.Find("[data-testid=dialog-save]").ClickAsync(new());

        dialog.WaitForElement("[data-testid=band-server-Overlap]", Wait);
        Assert.NotEmpty(dialog.FindAll("[data-testid=band-problem-0]"));
        Assert.NotEmpty(dialog.FindAll("[data-testid=band-problem-1]"));
    }

    [Fact]
    public async Task A_problem_that_names_no_band_marks_none()
    {
        var dialog = await TwoGoodBandsAsync();
        _scales.NextBandProblems = [new ApiProblemItem("Count", null, null)];

        await dialog.Find("[data-testid=dialog-save]").ClickAsync(new());

        dialog.WaitForElement("[data-testid=band-server-Count]", Wait);
        Assert.Empty(dialog.FindAll("[data-testid^=band-problem-]"));
    }

    [Fact]
    public async Task The_generic_alert_stays_and_the_details_clear_when_a_band_changes()
    {
        var dialog = await TwoGoodBandsAsync();
        _scales.NextBandProblems = [new ApiProblemItem("Gap", 49.99m, 50m)];
        await dialog.Find("[data-testid=dialog-save]").ClickAsync(new());
        dialog.WaitForElement("[data-testid=band-server-Gap]", Wait);
        Assert.NotNull(dialog.Find("[data-testid=scale-error]"));

        dialog.Find("[data-testid=band-max-0]").Input("50");

        Assert.Empty(dialog.FindAll("[data-testid=band-server-issues]")); // it no longer describes these bands
        Assert.Empty(dialog.FindAll("[data-testid^=band-problem-]"));
    }

    [Fact]
    public async Task The_fake_refuses_a_gap_the_way_the_server_does_with_the_range()
    {
        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => _scales.CreateScaleAsync(Unit, new SaveGradingScaleRequest("م", "S", ScaleKinds.Letter,
            [new(1, 0m, 40m, "F", null, null, null, null, null), new(2, 50m, 100m, "P", null, null, null, null, null)])));

        Assert.Equal(ProblemCodes.scale_bands_invalid, ex.Code);
        var gap = Assert.Single(ex.Problems);
        Assert.Equal(("Gap", 40m, 50m), (gap.Kind, gap.From, gap.To));
    }

    [Fact]
    public async Task Problems_are_read_from_the_problem_details_body()
    {
        var body = """{"status":400,"code":"scale_bands_invalid","problems":[{"kind":"Gap","from":40.0,"to":50},{"kind":"Overlap","from":10,"to":20.5},{"kind":"Count"}]}""";
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(body, Encoding.UTF8, "application/problem+json") };

        var ex = await ApiProblemException.FromResponseAsync(response);

        Assert.Equal(ProblemCodes.scale_bands_invalid, ex.Code);
        Assert.Equal([("Gap", 40m, 50m), ("Overlap", 10m, 20.5m), ("Count", null, null)], ex.Problems.Select(p => (p.Kind, p.From, p.To)));
    }

    [Fact]
    public async Task A_problem_body_without_problems_gives_an_empty_list()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("""{"code":"scale_in_use"}""", Encoding.UTF8, "application/problem+json") };

        var ex = await ApiProblemException.FromResponseAsync(response);

        Assert.Empty(ex.Problems);
    }

    // ---- read-only reasons ----

    [Fact]
    public async Task An_inherited_scale_names_the_unit_it_comes_from()
    {
        var inherited = _scales.SeedInherited(Unit, Head, "Head office scale");

        var dialog = await ShowAsync(inherited, readOnly: true, source: "Head office");

        Assert.Contains("Head office", dialog.Find("[data-testid=scale-readonly]").TextContent);
        Assert.Contains("Change it there", dialog.Find("[data-testid=scale-readonly]").TextContent);
    }

    [Fact]
    public async Task A_preset_says_it_is_a_preset_and_how_to_use_it()
    {
        var preset = (await _scales.ListScalesAsync(Unit)).First(s => s.IsTemplate);

        var dialog = await ShowAsync(preset, readOnly: true);

        Assert.Contains("preset", dialog.Find("[data-testid=scale-readonly]").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preset menu", dialog.Find("[data-testid=scale-readonly]").TextContent);
    }

    [Fact]
    public async Task Without_a_known_source_the_generic_reason_is_shown()
    {
        var inherited = _scales.SeedInherited(Unit, Head, "Scale");

        var dialog = await ShowAsync(inherited, readOnly: true, source: null);

        Assert.Contains("higher unit", dialog.Find("[data-testid=scale-readonly]").TextContent);
    }

    [Fact]
    public async Task Saving_a_change_to_a_scale_in_use_is_allowed_only_deleting_is_refused()
    {
        // The server refuses scale_in_use on DELETE only (BE #641 GradingScaleEndpoints): an edit of a scale a gradebook uses saves.
        var scale = await _scales.CreateScaleAsync(Unit, new SaveGradingScaleRequest("م", "Used", ScaleKinds.Letter, [new(1, 0m, 100m, "P", null, null, null, null, null)]));
        _scales.MarkInUse(scale.Id);
        var dialog = await ShowAsync((await _scales.GetScaleAsync(Unit, scale.Id)), readOnly: false);
        dialog.Find("[data-testid=band-code-0]").Input("Q");

        await dialog.Find("[data-testid=dialog-save]").ClickAsync(new());

        dialog.WaitForAssertion(async () => Assert.Equal("Q", (await _scales.GetScaleAsync(Unit, scale.Id)).Bands.Single().Code), Wait);
        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => _scales.DeleteScaleAsync(Unit, scale.Id));
        Assert.Equal(ProblemCodes.scale_in_use, ex.Code);
    }
}
