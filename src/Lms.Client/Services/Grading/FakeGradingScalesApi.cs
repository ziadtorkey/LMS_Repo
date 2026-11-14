using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Grading;

namespace Lms.Client.Services.Grading;

/// <summary>
/// In-memory <see cref="IGradingScalesApi"/> mimicking the rules the UI relies on: scale bands must tile 0 to 100 (<c>scale_bands_invalid</c>),
/// a scale in use cannot be deleted, a preset copy is provisional until an admin confirms it, one default per unit, template weights stay in 0-100.
/// </summary>
public sealed class FakeGradingScalesApi : IGradingScalesApi
{
    /// <summary>The unit the demo data belongs to; scales are kept per unit, the presets are global.</summary>
    public static readonly Guid DemoUnitId = Guid.Parse("00000000-0000-0000-0000-0000000e0001");

    private readonly List<GradingScaleDto> _scales = [];
    private readonly List<WeightTemplateDto> _templates = [];

    /// <summary>Ids of scales a gradebook uses (the fake gradebook adds its own).</summary>
    public HashSet<Guid> ScalesInUse { get; } = [];

    public FakeGradingScalesApi()
    {
        // Synthetic demo bands: the real preset numbers and descriptors are server data (the provisional seed), never part of a client release.
        _scales.Add(Preset("sa-percentage", "نموذج النسبة المئوية", "Percentage preset", ScaleKinds.Percentage, Demo(5)));
        _scales.Add(Preset("sa-letter", "نموذج التقدير الحرفي", "Letter preset", ScaleKinds.Letter, Demo(4)));
        _templates.Add(new WeightTemplateDto(Guid.NewGuid(), DemoUnitId, "توزيع قياسي", "Standard split", 100m, null,
            [new(Guid.NewGuid(), "أعمال الفصل", "Coursework", 40m, 1), new(Guid.NewGuid(), "الاختبارات", "Exams", 60m, 2)]));
    }

    /// <summary><paramref name="count"/> equal, clearly synthetic bands from 0 to 100 ("Band 1" is the highest).</summary>
    private static (string Code, decimal Min, decimal Max, string Ar, string En)[] Demo(int count)
    {
        var size = 100m / count;
        return [.. Enumerable.Range(0, count).Select(i =>
        {
            var from = Math.Round(100m - (i + 1) * size, 2);
            var to = i == 0 ? 100m : Math.Round(100m - i * size - 0.01m, 2);
            return ($"B{i + 1}", from, to, $"الفئة {i + 1}", $"Band {i + 1}");
        })];
    }

    private static GradingScaleDto Preset(string seed, string nameAr, string nameEn, string kind, (string Code, decimal Min, decimal Max, string Ar, string En)[] bands) =>
        new(Guid.NewGuid(), Guid.Empty, nameAr, nameEn, kind, false, true, true, seed, 1, false, null, false,
            [.. bands.Select((b, i) => new GradingScaleBandDto(Guid.NewGuid(), i + 1, b.Min, b.Max, b.Code, b.Ar, b.En, null, null, null))]);

    private static ApiProblemException Problem(int status, string code) => new(status, code, null, null);

    /// <summary>Flags the unedited copies of a preset as having an update (the server does this when the preset data changes).</summary>
    public void MarkUpdateAvailable(string seedKey)
    {
        for (var i = 0; i < _scales.Count; i++)
            if (!_scales[i].IsTemplate && _scales[i].SeedKey == seedKey) _scales[i] = _scales[i] with { UpdateAvailable = true };
    }

    /// <summary>Adds a scale the unit sees from a higher unit (read only here, <c>InheritedFromUnitId</c> set).</summary>
    public GradingScaleDto SeedInherited(Guid unitId, Guid sourceUnitId, string nameEn)
    {
        var scale = new GradingScaleDto(Guid.NewGuid(), unitId, "موروث", nameEn, ScaleKinds.Percentage, false, false, false, null, null, false, sourceUnitId, false,
            [new GradingScaleBandDto(Guid.NewGuid(), 1, 0m, 100m, "X", null, null, null, null, null)]);
        _scales.Add(scale);
        return scale;
    }

    /// <summary>Marks a scale as used by a gradebook (so it cannot be deleted).</summary>
    public void MarkInUse(Guid scaleId) => ScalesInUse.Add(scaleId);

    public Task<IReadOnlyList<GradingScaleDto>> ListScalesAsync(Guid unitId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GradingScaleDto>>([.. _scales.Where(s => s.IsTemplate || s.OrgUnitId == unitId).Select(WithUse)]);

    public Task<GradingScaleDto> GetScaleAsync(Guid unitId, Guid scaleId, CancellationToken ct = default) =>
        Task.FromResult(WithUse(Find(scaleId)));

    private GradingScaleDto Find(Guid id) => _scales.FirstOrDefault(s => s.Id == id) ?? throw Problem(404, ProblemCodes.not_found);

    private GradingScaleDto WithUse(GradingScaleDto s) => s with { InUse = ScalesInUse.Contains(s.Id) };

    /// <summary>
    /// The bands must be 1 to <see cref="GradingLimits.MaxBands"/>, start at 0, end at 100 and neither leave a gap nor overlap. Like the server
    /// (<c>scale_bands_invalid</c>), a refusal carries <c>problems[{kind, from, to}]</c>: each Gap, Overlap, Range or Count with its range.
    /// </summary>
    public static void ValidateBands(IReadOnlyList<SaveGradingScaleBandRequest> bands)
    {
        var problems = new List<ApiProblemItem>();
        if (bands.Count is 0 or > GradingLimits.MaxBands) problems.Add(new ApiProblemItem("Count", null, null));
        var sorted = bands.OrderBy(b => b.MinPercent).ToList();
        if (sorted.Count > 0)
        {
            if (sorted[0].MinPercent > 0m) problems.Add(new ApiProblemItem("Gap", 0m, sorted[0].MinPercent));
            foreach (var b in sorted.Where(b => b.MaxPercent < b.MinPercent)) problems.Add(new ApiProblemItem("Range", b.MinPercent, b.MaxPercent));
            for (var i = 1; i < sorted.Count; i++)
            {
                var gap = sorted[i].MinPercent - sorted[i - 1].MaxPercent;
                if (gap > 0.01m) problems.Add(new ApiProblemItem("Gap", sorted[i - 1].MaxPercent, sorted[i].MinPercent));
                else if (gap < 0m) problems.Add(new ApiProblemItem("Overlap", sorted[i].MinPercent, sorted[i - 1].MaxPercent));
            }

            if (sorted[^1].MaxPercent < 100m) problems.Add(new ApiProblemItem("Gap", sorted[^1].MaxPercent, 100m));
        }

        if (problems.Count > 0) throw new ApiProblemException(400, ProblemCodes.scale_bands_invalid, null, null, problems: problems);
    }

    /// <summary>When set, the next create or update is refused with these band problems (the server found what the client hint did not).</summary>
    public IReadOnlyList<ApiProblemItem>? NextBandProblems { get; set; }

    private void ThrowIfForced()
    {
        if (NextBandProblems is not { } forced) return;
        NextBandProblems = null;
        throw new ApiProblemException(400, ProblemCodes.scale_bands_invalid, null, null, problems: forced);
    }

    private static List<GradingScaleBandDto> ToBands(SaveGradingScaleRequest r) =>
        [.. r.Bands.OrderBy(b => b.MinPercent).Select((b, i) => new GradingScaleBandDto(Guid.NewGuid(), i + 1, b.MinPercent, b.MaxPercent, b.Code,
            b.LabelAr, b.LabelEn, b.DescriptorAr, b.DescriptorEn, b.Points))];

    private static void ValidateNames(SaveGradingScaleRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.NameAr) || string.IsNullOrWhiteSpace(r.NameEn)
            || r.NameAr.Length > GradingLimits.NameMaxLength || r.NameEn.Length > GradingLimits.NameMaxLength || !ScaleKinds.All.Contains(r.Kind))
            throw Problem(400, ProblemCodes.validation_failed);
    }

    public Task<GradingScaleDto> CreateScaleAsync(Guid unitId, SaveGradingScaleRequest request, CancellationToken ct = default)
    {
        ValidateNames(request);
        ThrowIfForced();
        ValidateBands(request.Bands);
        var scale = new GradingScaleDto(Guid.NewGuid(), unitId, request.NameAr, request.NameEn, request.Kind, false,
            false, false, null, null, false, null, false, ToBands(request));
        _scales.Add(scale);
        return Task.FromResult(scale);
    }

    public Task<GradingScaleDto> UpdateScaleAsync(Guid unitId, Guid scaleId, SaveGradingScaleRequest request, CancellationToken ct = default)
    {
        var current = Find(scaleId);
        if (current.IsTemplate || current.InheritedFromUnitId is not null || current.OrgUnitId != unitId) throw Problem(404, ProblemCodes.not_found);
        ValidateNames(request);
        ThrowIfForced();
        ValidateBands(request.Bands);
        // An edited copy no longer follows its preset.
        var updated = current with { NameAr = request.NameAr, NameEn = request.NameEn, Kind = request.Kind, Bands = ToBands(request), SeedKey = null, SeedVersion = null, UpdateAvailable = false };
        _scales[_scales.IndexOf(current)] = updated;
        return Task.FromResult(updated);
    }

    public Task DeleteScaleAsync(Guid unitId, Guid scaleId, CancellationToken ct = default)
    {
        var current = Find(scaleId);
        if (current.IsTemplate || current.OrgUnitId != unitId) throw Problem(404, ProblemCodes.not_found);
        if (ScalesInUse.Contains(scaleId)) throw Problem(409, ProblemCodes.scale_in_use);
        _scales.Remove(current);
        return Task.CompletedTask;
    }

    public Task<GradingScaleDto> ConfirmScaleAsync(Guid unitId, Guid scaleId, CancellationToken ct = default) =>
        Replace(scaleId, s => s with { Provisional = false });

    public Task<GradingScaleDto> SetDefaultScaleAsync(Guid unitId, Guid scaleId, CancellationToken ct = default)
    {
        foreach (var other in _scales.Where(s => s.OrgUnitId == unitId && s.IsDefault && s.Id != scaleId).ToList())
            _scales[_scales.IndexOf(other)] = other with { IsDefault = false };
        return Replace(scaleId, s => s with { IsDefault = true });
    }

    private Task<GradingScaleDto> Replace(Guid id, Func<GradingScaleDto, GradingScaleDto> change)
    {
        var current = Find(id);
        if (current.IsTemplate) throw Problem(404, ProblemCodes.not_found);
        var next = change(current);
        _scales[_scales.IndexOf(current)] = next;
        return Task.FromResult(next);
    }

    public Task<GradingScaleDto> CopyPresetAsync(Guid unitId, CopyScalePresetRequest request, CancellationToken ct = default)
    {
        var preset = _scales.FirstOrDefault(s => s.IsTemplate && s.SeedKey == request.SeedKey) ?? throw Problem(404, ProblemCodes.not_found);
        // An unedited copy flagged UpdateAvailable is updated in place from the preset instead of copied again.
        var stale = _scales.FirstOrDefault(s => !s.IsTemplate && s.OrgUnitId == unitId && s.SeedKey == request.SeedKey && s.UpdateAvailable);
        if (stale is not null)
        {
            var refreshed = stale with { Bands = [.. preset.Bands.Select(x => x with { Id = Guid.NewGuid() })], SeedVersion = preset.SeedVersion, UpdateAvailable = false, Provisional = true };
            _scales[_scales.IndexOf(stale)] = refreshed;
            return Task.FromResult(refreshed);
        }

        var copy = preset with { Id = Guid.NewGuid(), OrgUnitId = unitId, IsTemplate = false, IsDefault = false, Provisional = true, UpdateAvailable = false,
            Bands = [.. preset.Bands.Select(b => b with { Id = Guid.NewGuid() })] };
        _scales.Add(copy);
        return Task.FromResult(copy);
    }

    public Task<IReadOnlyList<WeightTemplateDto>> ListTemplatesAsync(Guid unitId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<WeightTemplateDto>>([.. _templates.Where(t => t.OrgUnitId == unitId)]);

    private static void ValidateTemplate(SaveWeightTemplateRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.NameAr) || string.IsNullOrWhiteSpace(r.NameEn)) throw Problem(400, ProblemCodes.validation_failed);
        if (r.Items.Any(i => i.Weight is < GradingLimits.WeightMin or > GradingLimits.WeightMax)) throw Problem(400, ProblemCodes.weights_invalid);
    }

    private static WeightTemplateDto Build(Guid id, Guid unitId, SaveWeightTemplateRequest r) =>
        new(id, unitId, r.NameAr, r.NameEn, r.Items.Sum(i => i.Weight), null,
            [.. r.Items.Select((i, n) => i with { Id = i.Id ?? Guid.NewGuid(), Order = n + 1 })]);

    public Task<WeightTemplateDto> CreateTemplateAsync(Guid unitId, SaveWeightTemplateRequest request, CancellationToken ct = default)
    {
        ValidateTemplate(request);
        var template = Build(Guid.NewGuid(), unitId, request);
        _templates.Add(template);
        return Task.FromResult(template);
    }

    public Task<WeightTemplateDto> UpdateTemplateAsync(Guid unitId, Guid templateId, SaveWeightTemplateRequest request, CancellationToken ct = default)
    {
        var current = _templates.FirstOrDefault(t => t.Id == templateId && t.OrgUnitId == unitId) ?? throw Problem(404, ProblemCodes.not_found);
        ValidateTemplate(request);
        var next = Build(templateId, unitId, request);
        _templates[_templates.IndexOf(current)] = next;
        return Task.FromResult(next);
    }

    public Task DeleteTemplateAsync(Guid unitId, Guid templateId, CancellationToken ct = default)
    {
        var current = _templates.FirstOrDefault(t => t.Id == templateId && t.OrgUnitId == unitId) ?? throw Problem(404, ProblemCodes.not_found);
        _templates.Remove(current);
        return Task.CompletedTask;
    }
}
