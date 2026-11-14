using Lms.Shared.Grading;

namespace Lms.Client.Services.Grading;

/// <summary>Grading scales and weight templates of an org unit (M5 wave 1, FR-GRD-01). Routes: <c>/api/org-units/{id}/grading-scales</c> and <c>/weight-templates</c>.</summary>
public interface IGradingScalesApi
{
    /// <summary>GET: the scales visible at the unit, including the ancestors' (read only, <c>InheritedFromUnitId</c> set) and the Saudi preset templates (<c>IsTemplate</c>).</summary>
    Task<IReadOnlyList<GradingScaleDto>> ListScalesAsync(Guid unitId, CancellationToken ct = default);

    Task<GradingScaleDto> GetScaleAsync(Guid unitId, Guid scaleId, CancellationToken ct = default);

    /// <summary>POST: 400 <c>scale_bands_invalid</c> for a gap, an overlap or more than <c>GradingLimits.MaxBands</c> bands.</summary>
    Task<GradingScaleDto> CreateScaleAsync(Guid unitId, SaveGradingScaleRequest request, CancellationToken ct = default);

    Task<GradingScaleDto> UpdateScaleAsync(Guid unitId, Guid scaleId, SaveGradingScaleRequest request, CancellationToken ct = default);

    /// <summary>DELETE: 409 <c>scale_in_use</c> when a gradebook uses it.</summary>
    Task DeleteScaleAsync(Guid unitId, Guid scaleId, CancellationToken ct = default);

    /// <summary>POST confirm: an admin confirms a provisional scale (L7).</summary>
    Task<GradingScaleDto> ConfirmScaleAsync(Guid unitId, Guid scaleId, CancellationToken ct = default);

    /// <summary>POST default: one default scale per unit.</summary>
    Task<GradingScaleDto> SetDefaultScaleAsync(Guid unitId, Guid scaleId, CancellationToken ct = default);

    /// <summary>POST presets: a provisional copy of a Saudi preset, or the update of an unedited copy.</summary>
    Task<GradingScaleDto> CopyPresetAsync(Guid unitId, CopyScalePresetRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<WeightTemplateDto>> ListTemplatesAsync(Guid unitId, CancellationToken ct = default);

    Task<WeightTemplateDto> CreateTemplateAsync(Guid unitId, SaveWeightTemplateRequest request, CancellationToken ct = default);

    Task<WeightTemplateDto> UpdateTemplateAsync(Guid unitId, Guid templateId, SaveWeightTemplateRequest request, CancellationToken ct = default);

    Task DeleteTemplateAsync(Guid unitId, Guid templateId, CancellationToken ct = default);
}
