using Lms.Shared;
using Lms.Shared.Organizations;

namespace Lms.Client.Services.Api;

/// <summary>Org templates: save a branch as a template, list, delete and apply (FR-ORG-07, FR-ORG-08).</summary>
public interface ITemplatesApi
{
    Task<IReadOnlyList<OrgTemplateSummaryDto>> ListAsync(CancellationToken ct = default);
    Task<OrgTemplateDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<OrgTemplateSummaryDto> SaveAsync(SaveTemplateRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ApplyTemplateResultDto> ApplyAsync(Guid id, ApplyTemplateRequest request, CancellationToken ct = default);
}

public sealed class TemplatesApi(ApiHttp http) : ITemplatesApi
{
    public async Task<IReadOnlyList<OrgTemplateSummaryDto>> ListAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<OrgTemplateSummaryDto>>(Routes.Templates, ct);

    public Task<OrgTemplateDetailDto> GetAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<OrgTemplateDetailDto>(Routes.Template(id), ct);

    public async Task<OrgTemplateSummaryDto> SaveAsync(SaveTemplateRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<OrgTemplateSummaryDto>(HttpMethod.Post, Routes.Templates, request, ct))!;

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.Template(id), null, ct);

    public async Task<ApplyTemplateResultDto> ApplyAsync(Guid id, ApplyTemplateRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ApplyTemplateResultDto>(HttpMethod.Post, Routes.TemplateApply(id), request, ct))!;
}

/// <summary>In-memory fake for the standalone dev server (Lms:UseFakeApi=true).</summary>
public sealed class FakeTemplatesApi(IOrgApi org) : ITemplatesApi
{
    private readonly List<(OrgTemplateSummaryDto Summary, List<TemplateNodeDto> Nodes)> _templates = [];
    private bool _seeded;

    private async Task SeedAsync()
    {
        if (_seeded) return;
        _seeded = true;
        var levels = await org.GetLevelTypesAsync();
        var school = levels.Skip(1).FirstOrDefault()?.Id ?? Guid.NewGuid();
        var grade = levels.Skip(2).FirstOrDefault()?.Id ?? Guid.NewGuid();
        var summary = new OrgTemplateSummaryDto(Guid.NewGuid(), "مدرسة نموذجية", "Standard school", TemplateVisibilities.Shared,
            Guid.NewGuid(), "مدارس النور", "Al Noor Schools", 3, ["Year"], DateTime.UtcNow.AddDays(-5));
        _templates.Add((summary,
        [
            new(1, null, school, "مدرسة {Year}", "School {Year}", "SCH-{Year}", false, null, false, ["CreateOrgUnits"], 2),
            new(2, 1, grade, "الصف الأول {Year}", "Grade 1 {Year}", "G1-{Year}", true, null, false, [], 0),
            new(3, 1, grade, "الصف الثاني {Year}", "Grade 2 {Year}", "G2-{Year}", true, null, false, [], 0),
        ]));
    }

    public async Task<IReadOnlyList<OrgTemplateSummaryDto>> ListAsync(CancellationToken ct = default)
    {
        await SeedAsync();
        return _templates.Select(t => t.Summary).ToList();
    }

    public async Task<OrgTemplateDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        await SeedAsync();
        var t = _templates.First(t => t.Summary.Id == id);
        return new OrgTemplateDetailDto(t.Summary, t.Nodes);
    }

    public async Task<OrgTemplateSummaryDto> SaveAsync(SaveTemplateRequest request, CancellationToken ct = default)
    {
        await SeedAsync();
        var placeholders = request.Parameterize.Values.Select(v => v.Trim('{', '}')).Distinct().ToList();
        var summary = new OrgTemplateSummaryDto(Guid.NewGuid(), request.NameAr, request.NameEn, request.Visibility,
            request.SourceOrgUnitId, "", "", 1, placeholders, DateTime.UtcNow);
        _templates.Add((summary, []));
        return summary;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await SeedAsync();
        _templates.RemoveAll(t => t.Summary.Id == id);
    }

    public async Task<ApplyTemplateResultDto> ApplyAsync(Guid id, ApplyTemplateRequest request, CancellationToken ct = default)
    {
        await SeedAsync();
        var t = _templates.First(t => t.Summary.Id == id);
        return new ApplyTemplateResultDto(Guid.NewGuid(), t.Nodes.Count == 0 ? 1 : t.Nodes.Count, ["Files.MaxUploadMb"]);
    }
}
