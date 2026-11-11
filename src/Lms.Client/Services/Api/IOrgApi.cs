using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;

namespace Lms.Client.Services.Api;

/// <summary>Organization structure: level types and org units (FR-ORG-02..04).</summary>
public interface IOrgApi
{
    Task<IReadOnlyList<LevelTypeDto>> GetLevelTypesAsync(CancellationToken ct = default);
    Task<LevelTypeDto> CreateLevelTypeAsync(SaveLevelTypeRequest request, CancellationToken ct = default);
    Task<LevelTypeDto> UpdateLevelTypeAsync(Guid id, SaveLevelTypeRequest request, CancellationToken ct = default);
    Task ReorderLevelTypesAsync(ReorderLevelTypesRequest request, CancellationToken ct = default);
    Task DeleteLevelTypeAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<OrgUnitNodeDto>> GetOrgUnitsAsync(string? search, CancellationToken ct = default);
    Task<OrgUnitNodeDto> CreateOrgUnitAsync(CreateOrgUnitRequest request, CancellationToken ct = default);
    Task<OrgUnitNodeDto> UpdateOrgUnitAsync(Guid id, UpdateOrgUnitRequest request, CancellationToken ct = default);
    Task SetOrgUnitStatusAsync(Guid id, SetOrgUnitStatusRequest request, CancellationToken ct = default);
    Task DeleteOrgUnitAsync(Guid id, CancellationToken ct = default);
    Task<OrgUnitNodeDto> GetOrgUnitAsync(Guid id, CancellationToken ct = default);
    Task MoveOrgUnitAsync(Guid id, MoveOrgUnitRequest request, CancellationToken ct = default);
    Task SetAcademicFlagsAsync(Guid id, OrgUnitAcademicFlagsRequest request, CancellationToken ct = default);
}

public sealed class OrgApi(ApiHttp http) : IOrgApi
{
    public Task<OrgUnitNodeDto> GetOrgUnitAsync(Guid id, CancellationToken ct = default) =>
        http.GetAsync<OrgUnitNodeDto>(Routes.OrgUnit(id), ct);

    public Task MoveOrgUnitAsync(Guid id, MoveOrgUnitRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.OrgUnitMove(id), request, ct);

    public Task SetAcademicFlagsAsync(Guid id, OrgUnitAcademicFlagsRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.OrgUnitAcademicFlags(id), request, ct);

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

    public async Task<IReadOnlyList<OrgUnitNodeDto>> GetOrgUnitsAsync(string? search, CancellationToken ct = default)
    {
        var query = "?includeInactive=true";
        if (!string.IsNullOrWhiteSpace(search)) query += $"&search={Uri.EscapeDataString(search)}";
        return await http.GetAsync<List<OrgUnitNodeDto>>(Routes.OrgUnits + query, ct);
    }

    public async Task<OrgUnitNodeDto> CreateOrgUnitAsync(CreateOrgUnitRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<OrgUnitNodeDto>(HttpMethod.Post, Routes.OrgUnits, request, ct))!;

    public async Task<OrgUnitNodeDto> UpdateOrgUnitAsync(Guid id, UpdateOrgUnitRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<OrgUnitNodeDto>(HttpMethod.Put, Routes.OrgUnit(id), request, ct))!;

    public Task SetOrgUnitStatusAsync(Guid id, SetOrgUnitStatusRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.OrgUnitStatus(id), request, ct);

    public Task DeleteOrgUnitAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.OrgUnit(id), null, ct);
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

    private readonly List<OrgUnitNodeDto> _units;

    public FakeOrgApi()
    {
        var group = Guid.NewGuid(); var north = Guid.NewGuid(); var south = Guid.NewGuid();
        OrgUnitNodeDto Unit(Guid id, Guid? parent, int level, string ar, string en, string code, int depth, string status = "Active", bool children = false, params string[] blocked) =>
            new(id, parent, _levels[level].Id, ar, en, code, depth, status, false, null, false, children, blocked);
        _units =
        [
            Unit(group, null, 0, "مدارس النور", "Al Noor Schools", "NOOR", 0, children: true),
            Unit(north, group, 1, "فرع الشمال", "North Branch", "NORTH", 1, children: true),
            Unit(south, group, 1, "فرع الجنوب", "South Branch", "SOUTH", 1, "Inactive"),
            Unit(Guid.NewGuid(), north, 2, "الصف الأول", "Grade 1", "N-G1", 2),
            Unit(Guid.NewGuid(), north, 2, "الصف الثاني", "Grade 2", "N-G2", 2, blocked: Permissions.OrgUnits.Create),
        ];
    }

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

    public Task<IReadOnlyList<OrgUnitNodeDto>> GetOrgUnitsAsync(string? search, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(search)) return Task.FromResult<IReadOnlyList<OrgUnitNodeDto>>(_units.ToList());
        bool Match(OrgUnitNodeDto u) => u.NameAr.Contains(search, StringComparison.OrdinalIgnoreCase)
            || u.NameEn.Contains(search, StringComparison.OrdinalIgnoreCase) || u.Code.Contains(search, StringComparison.OrdinalIgnoreCase);
        var keep = new HashSet<Guid>();
        foreach (var u in _units.Where(Match))
            for (OrgUnitNodeDto? c = u; c is not null; c = _units.FirstOrDefault(p => p.Id == c.ParentId)) keep.Add(c.Id);
        return Task.FromResult<IReadOnlyList<OrgUnitNodeDto>>(_units.Where(u => keep.Contains(u.Id)).ToList());
    }

    public Task<OrgUnitNodeDto> CreateOrgUnitAsync(CreateOrgUnitRequest request, CancellationToken ct = default)
    {
        var parentIndex = _units.FindIndex(u => u.Id == request.ParentId);
        var parent = _units[parentIndex];
        var dto = new OrgUnitNodeDto(Guid.NewGuid(), parent.Id, request.LevelTypeId, request.NameAr, request.NameEn, request.Code,
            parent.Depth + 1, "Active", false, null, false, false, []);
        _units.Add(dto);
        _units[parentIndex] = parent with { HasChildren = true };
        return Task.FromResult(dto);
    }

    public Task<OrgUnitNodeDto> UpdateOrgUnitAsync(Guid id, UpdateOrgUnitRequest request, CancellationToken ct = default)
    {
        var i = _units.FindIndex(u => u.Id == id);
        _units[i] = _units[i] with { LevelTypeId = request.LevelTypeId, NameAr = request.NameAr, NameEn = request.NameEn, Code = request.Code };
        return Task.FromResult(_units[i]);
    }

    public Task SetOrgUnitStatusAsync(Guid id, SetOrgUnitStatusRequest request, CancellationToken ct = default)
    {
        var i = _units.FindIndex(u => u.Id == id);
        _units[i] = _units[i] with { Status = request.Status };
        return Task.CompletedTask;
    }

    public Task<OrgUnitNodeDto> GetOrgUnitAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_units.First(u => u.Id == id));

    public Task MoveOrgUnitAsync(Guid id, MoveOrgUnitRequest request, CancellationToken ct = default)
    {
        var i = _units.FindIndex(u => u.Id == id);
        var parent = _units.First(u => u.Id == request.NewParentId);
        _units[i] = _units[i] with { ParentId = parent.Id, Depth = parent.Depth + 1 };
        return Task.CompletedTask;
    }

    public Task SetAcademicFlagsAsync(Guid id, OrgUnitAcademicFlagsRequest request, CancellationToken ct = default)
    {
        var i = _units.FindIndex(u => u.Id == id);
        _units[i] = _units[i] with
        {
            IsLevelTransfer = request.IsLevelTransfer, NextTransferUnitId = request.NextTransferUnitId,
            IsCourseManagementLevel = request.IsCourseManagementLevel,
        };
        return Task.CompletedTask;
    }

    public Task DeleteOrgUnitAsync(Guid id, CancellationToken ct = default)
    {
        if (_units.Any(u => u.ParentId == id)) throw new ApiProblemException(409, "unit_has_children", null, null);
        _units.RemoveAll(u => u.Id == id);
        return Task.CompletedTask;
    }
}
