using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Organizations;

namespace Lms.Client.Tests.Support;

public sealed class StubClientsApi : IClientsApi
{
    public List<ClientDto> Rows { get; } = [];
    public List<CreateClientRequest> Created { get; } = [];
    public List<(Guid Id, UpdateClientRequest Request)> Updated { get; } = [];
    public List<string?> Searches { get; } = [];
    public Exception? CreateFailure { get; set; }

    public Task<PagedResult<ClientDto>> ListAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        Searches.Add(search);
        var rows = Rows.Where(r => string.IsNullOrWhiteSpace(search) || r.NameEn.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        return Task.FromResult(new PagedResult<ClientDto>(rows, rows.Count, page, pageSize));
    }

    public Task<ClientDto> CreateAsync(CreateClientRequest request, CancellationToken ct = default)
    {
        if (CreateFailure is not null) return Task.FromException<ClientDto>(CreateFailure);
        Created.Add(request);
        var dto = new ClientDto(Guid.NewGuid(), request.NameAr, request.NameEn, request.Type, "Active", Guid.NewGuid(), DateTime.UtcNow);
        Rows.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<ClientDto> UpdateAsync(Guid id, UpdateClientRequest request, CancellationToken ct = default)
    {
        Updated.Add((id, request));
        var i = Rows.FindIndex(r => r.Id == id);
        Rows[i] = Rows[i] with { Status = request.Status };
        return Task.FromResult(Rows[i]);
    }
}

public sealed class StubOrgApi : IOrgApi
{
    public List<LevelTypeDto> Levels { get; } = [];
    public List<ReorderLevelTypesRequest> Reorders { get; } = [];
    public List<Guid> Deleted { get; } = [];
    public Exception? DeleteFailure { get; set; }

    public List<OrgUnitNodeDto> Units { get; } = [];
    public List<string?> UnitSearches { get; } = [];
    public List<CreateOrgUnitRequest> UnitsCreated { get; } = [];
    public List<(Guid Id, string Status)> StatusChanges { get; } = [];
    public List<Guid> UnitsDeleted { get; } = [];
    public Exception? UnitDeleteFailure { get; set; }
    public Exception? StatusFailure { get; set; }
    public Exception? ListFailure { get; set; }
    public Exception? LevelsFailure { get; set; }

    public List<(Guid Id, MoveOrgUnitRequest Request)> Moves { get; } = [];
    public List<(Guid Id, OrgUnitAcademicFlagsRequest Request)> FlagChanges { get; } = [];
    public Exception? MoveFailure { get; set; }

    public Task<OrgUnitNodeDto> GetOrgUnitAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Units.First(u => u.Id == id));

    public Task MoveOrgUnitAsync(Guid id, MoveOrgUnitRequest request, CancellationToken ct = default)
    {
        if (MoveFailure is not null) return Task.FromException(MoveFailure);
        Moves.Add((id, request));
        return Task.CompletedTask;
    }

    public Task SetAcademicFlagsAsync(Guid id, OrgUnitAcademicFlagsRequest request, CancellationToken ct = default)
    {
        FlagChanges.Add((id, request));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OrgUnitNodeDto>> GetOrgUnitsAsync(string? search, CancellationToken ct = default)
    {
        if (ListFailure is not null) return Task.FromException<IReadOnlyList<OrgUnitNodeDto>>(ListFailure);
        UnitSearches.Add(search);
        if (string.IsNullOrWhiteSpace(search)) return Task.FromResult<IReadOnlyList<OrgUnitNodeDto>>(Units.ToList());
        var keep = new HashSet<Guid>();
        foreach (var u in Units.Where(u => u.NameEn.Contains(search, StringComparison.OrdinalIgnoreCase)))
            for (OrgUnitNodeDto? c = u; c is not null; c = Units.FirstOrDefault(p => p.Id == c.ParentId)) keep.Add(c.Id);
        return Task.FromResult<IReadOnlyList<OrgUnitNodeDto>>(Units.Where(u => keep.Contains(u.Id)).ToList());
    }

    public Task<OrgUnitNodeDto> CreateOrgUnitAsync(CreateOrgUnitRequest request, CancellationToken ct = default)
    {
        UnitsCreated.Add(request);
        var dto = new OrgUnitNodeDto(Guid.NewGuid(), request.ParentId, request.LevelTypeId, request.NameAr, request.NameEn, request.Code, 1, "Active", false, null, false, false, []);
        Units.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<OrgUnitNodeDto> UpdateOrgUnitAsync(Guid id, UpdateOrgUnitRequest request, CancellationToken ct = default)
    {
        var i = Units.FindIndex(u => u.Id == id);
        Units[i] = Units[i] with { NameAr = request.NameAr, NameEn = request.NameEn, Code = request.Code, LevelTypeId = request.LevelTypeId };
        return Task.FromResult(Units[i]);
    }

    public Task SetOrgUnitStatusAsync(Guid id, SetOrgUnitStatusRequest request, CancellationToken ct = default)
    {
        if (StatusFailure is not null) return Task.FromException(StatusFailure);
        StatusChanges.Add((id, request.Status));
        var i = Units.FindIndex(u => u.Id == id);
        Units[i] = Units[i] with { Status = request.Status };
        return Task.CompletedTask;
    }

    public Task DeleteOrgUnitAsync(Guid id, CancellationToken ct = default)
    {
        if (UnitDeleteFailure is not null) return Task.FromException(UnitDeleteFailure);
        UnitsDeleted.Add(id);
        Units.RemoveAll(u => u.Id == id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LevelTypeDto>> GetLevelTypesAsync(CancellationToken ct = default) =>
        LevelsFailure is not null
            ? Task.FromException<IReadOnlyList<LevelTypeDto>>(LevelsFailure)
            : Task.FromResult<IReadOnlyList<LevelTypeDto>>(Levels.OrderBy(l => l.Order).ToList());

    public Task<LevelTypeDto> CreateLevelTypeAsync(SaveLevelTypeRequest request, CancellationToken ct = default)
    {
        var dto = new LevelTypeDto(Guid.NewGuid(), request.NameAr, request.NameEn, Levels.Count + 1, request.CanHostClasses, 0);
        Levels.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<LevelTypeDto> UpdateLevelTypeAsync(Guid id, SaveLevelTypeRequest request, CancellationToken ct = default)
    {
        var i = Levels.FindIndex(l => l.Id == id);
        Levels[i] = Levels[i] with { NameAr = request.NameAr, NameEn = request.NameEn, CanHostClasses = request.CanHostClasses };
        return Task.FromResult(Levels[i]);
    }

    public Task ReorderLevelTypesAsync(ReorderLevelTypesRequest request, CancellationToken ct = default)
    {
        Reorders.Add(request);
        for (var o = 0; o < request.OrderedIds.Count; o++)
        {
            var i = Levels.FindIndex(l => l.Id == request.OrderedIds[o]);
            Levels[i] = Levels[i] with { Order = o + 1 };
        }
        return Task.CompletedTask;
    }

    public Task DeleteLevelTypeAsync(Guid id, CancellationToken ct = default)
    {
        if (DeleteFailure is not null) return Task.FromException(DeleteFailure);
        Deleted.Add(id);
        Levels.RemoveAll(l => l.Id == id);
        return Task.CompletedTask;
    }
}
