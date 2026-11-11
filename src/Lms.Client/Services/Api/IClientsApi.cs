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
        new(Guid.NewGuid(), "مدارس النور", "Al Noor Schools", OrganizationTypes.SchoolGroup, OrganizationStatuses.Active, Guid.NewGuid(), DateTime.UtcNow.AddDays(-40), "noor"),
        new(Guid.NewGuid(), "جامعة الأفق", "Horizon University", OrganizationTypes.University, OrganizationStatuses.Active, Guid.NewGuid(), DateTime.UtcNow.AddDays(-12), "horizon"),
        new(Guid.NewGuid(), "مدارس الفجر", "Al Fajr Schools", OrganizationTypes.SchoolGroup, OrganizationStatuses.Suspended, Guid.NewGuid(), DateTime.UtcNow.AddDays(-3), "fajr"),
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
        var shortCode = request.ShortCode ?? request.RootUnitCode.ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(shortCode, "^[a-z0-9-]{2,20}$"))
            return Task.FromException<ClientDto>(new ApiProblemException(400, "invalid_short_code", null, null));
        if (_clients.Any(c => c.ShortCode == shortCode))
            return Task.FromException<ClientDto>(new ApiProblemException(409, "short_code_taken", null, null));
        var dto = new ClientDto(Guid.NewGuid(), request.NameAr, request.NameEn, request.Type, OrganizationStatuses.Active, Guid.NewGuid(), DateTime.UtcNow, shortCode);
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
