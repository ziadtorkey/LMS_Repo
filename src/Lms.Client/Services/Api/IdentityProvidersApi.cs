using Lms.Shared;
using Lms.Shared.IdentityProviders;

namespace Lms.Client.Services.Api;

/// <summary>A client's single sign-on providers (FR-SEC-08). The client secret is write-only.</summary>
public interface IIdentityProvidersApi
{
    Task<IReadOnlyList<IdentityProviderDto>> ListAsync(CancellationToken ct = default);
    Task<IdentityProviderDto> CreateAsync(SaveIdentityProviderRequest request, CancellationToken ct = default);
    Task<IdentityProviderDto> UpdateAsync(Guid id, SaveIdentityProviderRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ProviderTestResultDto> TestAsync(Guid id, CancellationToken ct = default);
}

public sealed class IdentityProvidersApi(ApiHttp http) : IIdentityProvidersApi
{
    public async Task<IReadOnlyList<IdentityProviderDto>> ListAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<IdentityProviderDto>>(Routes.IdentityProviders, ct);

    public async Task<IdentityProviderDto> CreateAsync(SaveIdentityProviderRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<IdentityProviderDto>(HttpMethod.Post, Routes.IdentityProviders, request, ct))!;

    public async Task<IdentityProviderDto> UpdateAsync(Guid id, SaveIdentityProviderRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<IdentityProviderDto>(HttpMethod.Put, Routes.IdentityProvider(id), request, ct))!;

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.IdentityProvider(id), null, ct);

    public async Task<ProviderTestResultDto> TestAsync(Guid id, CancellationToken ct = default) =>
        (await http.SendAsync<ProviderTestResultDto>(HttpMethod.Post, Routes.IdentityProviderTest(id), null, ct))!;
}

/// <summary>In-memory fake for the standalone dev server and bUnit tests. A tenant or authority containing "bad" fails the test call.</summary>
public sealed class FakeIdentityProvidersApi : IIdentityProvidersApi
{
    private readonly List<IdentityProviderDto> _items =
    [
        new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), IdentityProviderKinds.AzureAd, "school-azure", "مايكروسوفت المدرسة", "School Microsoft",
            "https://login.microsoftonline.com/11111111-2222-3333-4444-555555555555/v2.0", "11111111-2222-3333-4444-555555555555",
            "app-1", true, "openid profile email", ExternalMatchBy.Email, null, ["school.example"], true,
            "https://lms.example/signin-external/school-azure"),
    ];

    public List<SaveIdentityProviderRequest> Saved { get; } = [];
    public List<Guid> Tested { get; } = [];

    public Task<IReadOnlyList<IdentityProviderDto>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IdentityProviderDto>>(_items.ToList());

    public Task<IdentityProviderDto> CreateAsync(SaveIdentityProviderRequest r, CancellationToken ct = default)
    {
        Saved.Add(r);
        var dto = ToDto(Guid.NewGuid(), r, !string.IsNullOrEmpty(r.ClientSecret));
        _items.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<IdentityProviderDto> UpdateAsync(Guid id, SaveIdentityProviderRequest r, CancellationToken ct = default)
    {
        Saved.Add(r);
        var index = _items.FindIndex(p => p.Id == id);
        var dto = ToDto(id, r, r.ClientSecret is not null || _items[index].HasClientSecret);
        _items[index] = dto;
        return Task.FromResult(dto);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _items.RemoveAll(p => p.Id == id);
        return Task.CompletedTask;
    }

    public Task<ProviderTestResultDto> TestAsync(Guid id, CancellationToken ct = default)
    {
        Tested.Add(id);
        var p = _items.First(x => x.Id == id);
        return Task.FromResult(p.Authority.Contains("bad", StringComparison.OrdinalIgnoreCase)
            ? new ProviderTestResultDto(false, null, "discovery document not found")
            : new ProviderTestResultDto(true, p.Authority.Replace("/v2.0", ""), null));
    }

    private static IdentityProviderDto ToDto(Guid id, SaveIdentityProviderRequest r, bool hasSecret)
    {
        var authority = r.Authority ?? $"https://login.microsoftonline.com/{r.AzureTenantId}/v2.0";
        return new(id, r.Kind, r.Slug, r.DisplayNameAr, r.DisplayNameEn, authority, r.AzureTenantId, r.ClientId, hasSecret,
            r.Scopes, r.MatchBy, r.NationalIdClaim, r.EmailDomains, r.Enabled, $"https://lms.example/signin-external/{r.Slug}");
    }
}
