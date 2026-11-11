using Lms.Client.Services.Api;
using Lms.Shared.Organizations;

namespace Lms.Client.Tests.Support;

public sealed class StubTemplatesApi : ITemplatesApi
{
    public List<OrgTemplateSummaryDto> Items { get; } = [];
    public Dictionary<Guid, IReadOnlyList<TemplateNodeDto>> Nodes { get; } = [];
    public List<SaveTemplateRequest> Saved { get; } = [];
    public List<Guid> Deleted { get; } = [];
    public List<(Guid Id, ApplyTemplateRequest Request)> Applied { get; } = [];
    public Exception? DeleteFailure { get; set; }
    public ApplyTemplateResultDto ApplyResult { get; set; } = new(Guid.NewGuid(), 3, []);

    public Task<IReadOnlyList<OrgTemplateSummaryDto>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OrgTemplateSummaryDto>>(Items.ToList());

    public Task<OrgTemplateDetailDto> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(new OrgTemplateDetailDto(Items.First(i => i.Id == id), Nodes.GetValueOrDefault(id) ?? []));

    public Task<OrgTemplateSummaryDto> SaveAsync(SaveTemplateRequest request, CancellationToken ct = default)
    {
        Saved.Add(request);
        var dto = new OrgTemplateSummaryDto(Guid.NewGuid(), request.NameAr, request.NameEn, request.Visibility,
            request.SourceOrgUnitId, "م", "Owner", 1, request.Parameterize.Values.ToList(), DateTime.UtcNow);
        Items.Add(dto);
        return Task.FromResult(dto);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (DeleteFailure is not null) return Task.FromException(DeleteFailure);
        Deleted.Add(id);
        Items.RemoveAll(i => i.Id == id);
        return Task.CompletedTask;
    }

    public Task<ApplyTemplateResultDto> ApplyAsync(Guid id, ApplyTemplateRequest request, CancellationToken ct = default)
    {
        Applied.Add((id, request));
        return Task.FromResult(ApplyResult);
    }
}
