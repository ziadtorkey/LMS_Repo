using Lms.Client.Services.Api;
using Lms.Client.Services.Grading;
using Lms.Shared;
using Lms.Shared.Grading;

namespace Lms.Client.Tests.Grading;

/// <summary>The fake scales mimic the rules the admin screens depend on (M5 Task 22).</summary>
public sealed class FakeGradingScalesApiTests
{
    private static readonly Guid Unit = FakeGradingScalesApi.DemoUnitId;
    private readonly FakeGradingScalesApi _api = new();

    private static SaveGradingScaleBandRequest Band(string code, decimal min, decimal max) => new(0, min, max, code, code, code, null, null, null);

    private static SaveGradingScaleRequest Scale(params SaveGradingScaleBandRequest[] bands) => new("مقياس", "Scale", ScaleKinds.Letter, bands);

    private static Task<ApiProblemException> ProblemAsync(Func<Task> call) => Assert.ThrowsAsync<ApiProblemException>(call);

    [Fact]
    public async Task The_list_includes_the_saudi_presets_as_templates()
    {
        var scales = await _api.ListScalesAsync(Unit);

        Assert.Contains(scales, s => s.IsTemplate && s.SeedKey == "sa-percentage" && s.Provisional);
    }

    [Fact]
    public async Task Copying_a_preset_makes_a_provisional_scale_of_the_unit_until_it_is_confirmed()
    {
        var copy = await _api.CopyPresetAsync(Unit, new CopyScalePresetRequest("sa-letter"));
        Assert.True(copy.Provisional);
        Assert.False(copy.IsTemplate);
        Assert.Equal(Unit, copy.OrgUnitId);

        var confirmed = await _api.ConfirmScaleAsync(Unit, copy.Id);

        Assert.False(confirmed.Provisional);
    }

    [Fact]
    public async Task Bands_that_leave_a_gap_are_refused()
    {
        var ex = await ProblemAsync(() => _api.CreateScaleAsync(Unit, Scale(Band("A", 50m, 100m), Band("F", 0m, 40m))));

        Assert.Equal(ProblemCodes.scale_bands_invalid, ex.Code);
    }

    [Fact]
    public async Task Bands_that_overlap_are_refused()
    {
        var ex = await ProblemAsync(() => _api.CreateScaleAsync(Unit, Scale(Band("A", 50m, 100m), Band("F", 0m, 60m))));

        Assert.Equal(ProblemCodes.scale_bands_invalid, ex.Code);
    }

    [Fact]
    public async Task Bands_must_cover_zero_to_one_hundred()
    {
        var ex = await ProblemAsync(() => _api.CreateScaleAsync(Unit, Scale(Band("A", 10m, 100m))));

        Assert.Equal(ProblemCodes.scale_bands_invalid, ex.Code);
    }

    [Fact]
    public async Task A_valid_scale_is_created_with_its_bands_in_order()
    {
        var scale = await _api.CreateScaleAsync(Unit, Scale(Band("P", 50m, 100m), Band("F", 0m, 49.99m)));

        Assert.Equal(["F", "P"], scale.Bands.Select(b => b.Code));
        Assert.Equal([1, 2], scale.Bands.Select(b => b.Order));
    }

    [Fact]
    public async Task A_scale_in_use_cannot_be_deleted()
    {
        var scale = await _api.CreateScaleAsync(Unit, Scale(Band("A", 0m, 100m)));
        _api.MarkInUse(scale.Id);

        var ex = await ProblemAsync(() => _api.DeleteScaleAsync(Unit, scale.Id));

        Assert.Equal(ProblemCodes.scale_in_use, ex.Code);
        Assert.Equal(409, ex.Status);
    }

    [Fact]
    public async Task A_preset_template_cannot_be_edited_or_deleted()
    {
        var preset = (await _api.ListScalesAsync(Unit)).First(s => s.IsTemplate);

        var edit = await ProblemAsync(() => _api.UpdateScaleAsync(Unit, preset.Id, Scale(Band("A", 0m, 100m))));
        var delete = await ProblemAsync(() => _api.DeleteScaleAsync(Unit, preset.Id));

        Assert.Equal(404, edit.Status);
        Assert.Equal(404, delete.Status);
    }

    [Fact]
    public async Task Editing_a_copy_detaches_it_from_its_preset()
    {
        var copy = await _api.CopyPresetAsync(Unit, new CopyScalePresetRequest("sa-percentage"));

        var edited = await _api.UpdateScaleAsync(Unit, copy.Id, Scale(Band("A", 0m, 100m)));

        Assert.Null(edited.SeedKey);
    }

    [Fact]
    public async Task There_is_one_default_scale_per_unit()
    {
        var a = await _api.CreateScaleAsync(Unit, Scale(Band("A", 0m, 100m)));
        var b = await _api.CreateScaleAsync(Unit, Scale(Band("B", 0m, 100m)));

        await _api.SetDefaultScaleAsync(Unit, a.Id);
        await _api.SetDefaultScaleAsync(Unit, b.Id);

        var scales = await _api.ListScalesAsync(Unit);
        Assert.Single(scales, s => s.IsDefault);
        Assert.True(scales.First(s => s.Id == b.Id).IsDefault);
    }

    [Fact]
    public async Task Template_weights_stay_in_range_and_are_totalled()
    {
        var bad = await ProblemAsync(() => _api.CreateTemplateAsync(Unit, new SaveWeightTemplateRequest("ق", "T", [new WeightTemplateItemDto(null, "أ", "A", 120m, 1)])));
        var good = await _api.CreateTemplateAsync(Unit, new SaveWeightTemplateRequest("ق", "T", [new(null, "أ", "A", 30m, 1), new(null, "ب", "B", 70m, 2)]));

        Assert.Equal(ProblemCodes.weights_invalid, bad.Code);
        Assert.Equal(100m, good.WeightsTotal);
    }

    [Fact]
    public async Task Templates_are_listed_per_unit_and_can_be_deleted()
    {
        var created = await _api.CreateTemplateAsync(Unit, new SaveWeightTemplateRequest("ق", "T", [new(null, "أ", "A", 100m, 1)]));
        Assert.Contains(await _api.ListTemplatesAsync(Unit), t => t.Id == created.Id);
        Assert.Empty(await _api.ListTemplatesAsync(Guid.NewGuid()));

        await _api.DeleteTemplateAsync(Unit, created.Id);

        Assert.DoesNotContain(await _api.ListTemplatesAsync(Unit), t => t.Id == created.Id);
    }
}
