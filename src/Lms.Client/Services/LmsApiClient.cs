using System.Net;
using System.Net.Http.Json;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Me;
using Microsoft.AspNetCore.Components;

namespace Lms.Client.Services;

/// <summary>Client-side view of the API. Faked in dev/tests until the backend endpoint exists.</summary>
public interface ILmsApi
{
    Task<MeDto> GetMeAsync(CancellationToken ct = default);
    Task<EffectivePermissionsDto> GetMyPermissionsAsync(CancellationToken ct = default);
}

public sealed class LmsApiClient(HttpClient http, IContextStore context) : ILmsApi
{
    public Task<MeDto> GetMeAsync(CancellationToken ct = default) => GetAsync<MeDto>(Routes.Me, ct);

    public Task<EffectivePermissionsDto> GetMyPermissionsAsync(CancellationToken ct = default) =>
        GetAsync<EffectivePermissionsDto>(Routes.MyPermissions, ct);

    private async Task<T> GetAsync<T>(string route, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route.TrimStart('/'));
        if (context.ActiveAssignmentId is { } id)
            request.Headers.Add(ContextHeader.Name, id.ToString());
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }
}

/// <summary>Sends the user to the Forbidden page whenever the API answers 403.</summary>
public sealed class ForbiddenRedirectHandler(NavigationManager navigation) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            navigation.NavigateTo("forbidden");
        return response;
    }
}
