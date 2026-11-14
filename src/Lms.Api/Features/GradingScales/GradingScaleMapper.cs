using Lms.Domain.Grading;
using Lms.Shared.Grading;

namespace Lms.Api.Features.GradingScales;

internal static class GradingScaleMapper
{
    public static async Task<GradingScaleDto> ToDtoAsync(
        VisibleScale v, Guid viewedUnitId, IScalePresetCatalog catalog, IScaleUsage usage, CancellationToken ct)
    {
        var s = v.Scale;
        var latest = s.SeedKey is null ? null : catalog.Presets.FirstOrDefault(p => p.Key == s.SeedKey)?.Version;
        var updateAvailable = !s.IsTemplate && !s.Edited && latest is { } l && s.SeedVersion < l;
        return new GradingScaleDto(
            s.Id, s.OrgUnitId, s.NameAr, s.NameEn, s.Kind, s.IsDefault, s.IsTemplate, s.Provisional, s.SeedKey, s.SeedVersion,
            updateAvailable, s.OrgUnitId == viewedUnitId ? null : s.OrgUnitId, await usage.IsInUseAsync(s.Id, ct),
            v.Bands.Select(b => new GradingScaleBandDto(
                b.Id, b.Order, b.MinPercent, b.MaxPercent, b.Code, b.LabelAr, b.LabelEn, b.DescriptorAr, b.DescriptorEn, b.Points)).ToList());
    }

    public static async Task<IReadOnlyList<GradingScaleDto>> ToDtosAsync(
        IReadOnlyList<VisibleScale> scales, Guid viewedUnitId, IScalePresetCatalog catalog, IScaleUsage usage, CancellationToken ct)
    {
        var list = new List<GradingScaleDto>(scales.Count);
        foreach (var scale in scales)
        {
            list.Add(await ToDtoAsync(scale, viewedUnitId, catalog, usage, ct));
        }

        return list;
    }

    public static WeightTemplateDto ToDto(VisibleWeightTemplate v, Guid viewedUnitId) =>
        new(v.Template.Id, v.Template.OrgUnitId, v.Template.NameAr, v.Template.NameEn, v.Items.Sum(i => i.Weight),
            v.Template.OrgUnitId == viewedUnitId ? null : v.Template.OrgUnitId,
            v.Items.Select(i => new WeightTemplateItemDto(i.Id, i.NameAr, i.NameEn, i.Weight, i.Order)).ToList());
}
