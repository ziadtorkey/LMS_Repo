using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Grading;

namespace Lms.Client.Services.Grading;

public sealed class GradingScalesApi(ApiHttp http) : IGradingScalesApi
{
    public async Task<IReadOnlyList<GradingScaleDto>> ListScalesAsync(Guid unitId, CancellationToken ct = default) =>
        await http.GetAsync<List<GradingScaleDto>>(Routes.OrgUnitGradingScales(unitId), ct);

    public Task<GradingScaleDto> GetScaleAsync(Guid unitId, Guid scaleId, CancellationToken ct = default) =>
        http.GetAsync<GradingScaleDto>(Routes.GradingScale(unitId, scaleId), ct);

    public async Task<GradingScaleDto> CreateScaleAsync(Guid unitId, SaveGradingScaleRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<GradingScaleDto>(HttpMethod.Post, Routes.OrgUnitGradingScales(unitId), request, ct))!;

    public async Task<GradingScaleDto> UpdateScaleAsync(Guid unitId, Guid scaleId, SaveGradingScaleRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<GradingScaleDto>(HttpMethod.Put, Routes.GradingScale(unitId, scaleId), request, ct))!;

    public Task DeleteScaleAsync(Guid unitId, Guid scaleId, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.GradingScale(unitId, scaleId), null, ct);

    public async Task<GradingScaleDto> ConfirmScaleAsync(Guid unitId, Guid scaleId, CancellationToken ct = default) =>
        (await http.SendAsync<GradingScaleDto>(HttpMethod.Post, Routes.GradingScaleConfirm(unitId, scaleId), null, ct))!;

    public async Task<GradingScaleDto> SetDefaultScaleAsync(Guid unitId, Guid scaleId, CancellationToken ct = default) =>
        (await http.SendAsync<GradingScaleDto>(HttpMethod.Post, Routes.GradingScaleDefault(unitId, scaleId), null, ct))!;

    public async Task<GradingScaleDto> CopyPresetAsync(Guid unitId, CopyScalePresetRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<GradingScaleDto>(HttpMethod.Post, Routes.OrgUnitGradingScalePresets(unitId), request, ct))!;

    public async Task<IReadOnlyList<WeightTemplateDto>> ListTemplatesAsync(Guid unitId, CancellationToken ct = default) =>
        await http.GetAsync<List<WeightTemplateDto>>(Routes.OrgUnitWeightTemplates(unitId), ct);

    public async Task<WeightTemplateDto> CreateTemplateAsync(Guid unitId, SaveWeightTemplateRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<WeightTemplateDto>(HttpMethod.Post, Routes.OrgUnitWeightTemplates(unitId), request, ct))!;

    public async Task<WeightTemplateDto> UpdateTemplateAsync(Guid unitId, Guid templateId, SaveWeightTemplateRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<WeightTemplateDto>(HttpMethod.Put, Routes.WeightTemplate(unitId, templateId), request, ct))!;

    public Task DeleteTemplateAsync(Guid unitId, Guid templateId, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.WeightTemplate(unitId, templateId), null, ct);
}
