using Lms.Shared;
using Lms.Shared.Organizations;

namespace Lms.Client.Services.Api;

/// <summary>Platform-admin client management (FR-ORG-01).</summary>
public interface IClientsApi
{
    Task<PagedResult<ClientDto>> ListAsync(int page, int pageSize, string? search, CancellationToken ct = default);
    Task<ClientDto> CreateAsync(CreateClientRequest request, CancellationToken ct = default);
    Task<ClientDto> UpdateAsync(Guid id, UpdateClientRequest request, CancellationToken ct = default);
}

public sealed class ClientsApi(ApiHttp http) : IClientsApi
{
    public Task<PagedResult<ClientDto>> ListAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        var query = $"?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search)) query += $"&search={Uri.EscapeDataString(search)}";
        return http.GetAsync<PagedResult<ClientDto>>(Routes.Clients + query, ct);
    }

    public async Task<ClientDto> CreateAsync(CreateClientRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ClientDto>(HttpMethod.Post, Routes.Clients, request, ct))!;

    public async Task<ClientDto> UpdateAsync(Guid id, UpdateClientRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<ClientDto>(HttpMethod.Put, Routes.Client(id), request, ct))!;
}

/// <summary>In-memory fake for the standalone dev server (Lms:UseFakeApi=true).</summary>
public sealed class FakeClientsApi : IClientsApi
{
    private readonly List<ClientDto> _clients =
    [
        new(Guid.NewGuid(), "مدارس النور", "Al Noor Schools", OrganizationTypes.SchoolGroup, OrganizationStatuses.Active, Guid.NewGuid(), DateTime.UtcNow.AddDays(-40)),
        new(Guid.NewGuid(), "جامعة الأفق", "Horizon University", OrganizationTypes.University, OrganizationStatuses.Active, Guid.NewGuid(), DateTime.UtcNow.AddDays(-12)),
        new(Guid.NewGuid(), "مدارس الفجر", "Al Fajr Schools", OrganizationTypes.SchoolGroup, OrganizationStatuses.Suspended, Guid.NewGuid(), DateTime.UtcNow.AddDays(-3)),
    ];

    public Task<PagedResult<ClientDto>> ListAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        var rows = _clients.Where(c => string.IsNullOrWhiteSpace(search)
            || c.NameAr.Contains(search, StringComparison.OrdinalIgnoreCase)
            || c.NameEn.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        return Task.FromResult(new PagedResult<ClientDto>(rows.Skip((page - 1) * pageSize).Take(pageSize).ToList(), rows.Count, page, pageSize));
    }

    public Task<ClientDto> CreateAsync(CreateClientRequest request, CancellationToken ct = default)
    {
        var dto = new ClientDto(Guid.NewGuid(), request.NameAr, request.NameEn, request.Type, OrganizationStatuses.Active, Guid.NewGuid(), DateTime.UtcNow);
        _clients.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<ClientDto> UpdateAsync(Guid id, UpdateClientRequest request, CancellationToken ct = default)
    {
        var index = _clients.FindIndex(c => c.Id == id);
        _clients[index] = _clients[index] with { NameAr = request.NameAr, NameEn = request.NameEn, Status = request.Status };
        return Task.FromResult(_clients[index]);
    }
}
