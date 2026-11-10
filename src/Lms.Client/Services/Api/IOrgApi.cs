using Lms.Shared;
using Lms.Shared.Organizations;

namespace Lms.Client.Services.Api;

/// <summary>Organization structure: level types now, org units in later tasks (FR-ORG-02..).</summary>
public interface IOrgApi
{
    Task<IReadOnlyList<LevelTypeDto>> GetLevelTypesAsync(CancellationToken ct = default);
    Task<LevelTypeDto> CreateLevelTypeAsync(SaveLevelTypeRequest request, CancellationToken ct = default);
    Task<LevelTypeDto> UpdateLevelTypeAsync(Guid id, SaveLevelTypeRequest request, CancellationToken ct = default);
    Task ReorderLevelTypesAsync(ReorderLevelTypesRequest request, CancellationToken ct = default);
    Task DeleteLevelTypeAsync(Guid id, CancellationToken ct = default);
}

public sealed class OrgApi(ApiHttp http) : IOrgApi
{
    public async Task<IReadOnlyList<LevelTypeDto>> GetLevelTypesAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<LevelTypeDto>>(Routes.LevelTypes, ct);

    public async Task<LevelTypeDto> CreateLevelTypeAsync(SaveLevelTypeRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<LevelTypeDto>(HttpMethod.Post, Routes.LevelTypes, request, ct))!;

    public async Task<LevelTypeDto> UpdateLevelTypeAsync(Guid id, SaveLevelTypeRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<LevelTypeDto>(HttpMethod.Put, Routes.LevelType(id), request, ct))!;

    public Task ReorderLevelTypesAsync(ReorderLevelTypesRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.LevelTypesOrder, request, ct);

    public Task DeleteLevelTypeAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.LevelType(id), null, ct);
}

/// <summary>In-memory fake for the standalone dev server (Lms:UseFakeApi=true).</summary>
public sealed class FakeOrgApi : IOrgApi
{
    private readonly List<LevelTypeDto> _levels =
    [
        new(Guid.NewGuid(), "المجموعة", "Group", 1, false, 1),
        new(Guid.NewGuid(), "المدرسة", "School", 2, false, 3),
        new(Guid.NewGuid(), "الصف", "Grade", 3, true, 12),
    ];

    public Task<IReadOnlyList<LevelTypeDto>> GetLevelTypesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LevelTypeDto>>(_levels.OrderBy(l => l.Order).ToList());

    public Task<LevelTypeDto> CreateLevelTypeAsync(SaveLevelTypeRequest request, CancellationToken ct = default)
    {
        var dto = new LevelTypeDto(Guid.NewGuid(), request.NameAr, request.NameEn, _levels.Count + 1, request.CanHostClasses, 0);
        _levels.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<LevelTypeDto> UpdateLevelTypeAsync(Guid id, SaveLevelTypeRequest request, CancellationToken ct = default)
    {
        var i = _levels.FindIndex(l => l.Id == id);
        _levels[i] = _levels[i] with { NameAr = request.NameAr, NameEn = request.NameEn, CanHostClasses = request.CanHostClasses };
        return Task.FromResult(_levels[i]);
    }

    public Task ReorderLevelTypesAsync(ReorderLevelTypesRequest request, CancellationToken ct = default)
    {
        for (var order = 0; order < request.OrderedIds.Count; order++)
        {
            var i = _levels.FindIndex(l => l.Id == request.OrderedIds[order]);
            _levels[i] = _levels[i] with { Order = order + 1 };
        }
        return Task.CompletedTask;
    }

    public Task DeleteLevelTypeAsync(Guid id, CancellationToken ct = default)
    {
        var level = _levels.First(l => l.Id == id);
        if (level.UnitCount > 0)
            throw new ApiProblemException(409, "level_type_in_use", null, null);
        _levels.Remove(level);
        return Task.CompletedTask;
    }
}
