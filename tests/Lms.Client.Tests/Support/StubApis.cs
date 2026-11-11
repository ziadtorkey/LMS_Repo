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

    public Task<IReadOnlyList<LevelTypeDto>> GetLevelTypesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LevelTypeDto>>(Levels.OrderBy(l => l.Order).ToList());

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
