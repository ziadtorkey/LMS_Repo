using System.Net;
using System.Net.Http.Json;
using Lms.Client.Services.Api;
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
        if (!response.IsSuccessStatusCode)
            throw await ApiProblemException.ClassifyAsync(response, HttpMethod.Get, ct);
        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }
}

/// <summary>The API answered 403; callers show the Forbidden page instead of an error state.</summary>
public sealed class ApiForbiddenException : HttpRequestException
{
    public ApiForbiddenException() : base("Forbidden", null, HttpStatusCode.Forbidden) { }
}

/// <summary>
/// Central 403 handling for every API call:
/// a missing/stale context (invalid_context, context_required) → clear the stored context and go to the chooser (bug #84);
/// a forbidden GET → Forbidden page. A forbidden action (POST/PUT/DELETE) keeps the page so the caller can show the problem.
/// </summary>
public sealed class ForbiddenRedirectHandler(NavigationManager navigation, IContextStore context) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Forbidden) return response;

        // Buffer so the caller can still read the body after we peek at the problem code.
        await response.Content.LoadIntoBufferAsync(ct);
        var problem = await ApiProblemException.FromResponseAsync(response, ct);
        if (problem.IsContextProblem)
        {
            if (problem.Code == ApiProblemException.InvalidContext)
                await context.ClearAsync();
            navigation.NavigateTo("choose-context");
        }
        else if (request.Method == HttpMethod.Get)
        {
            navigation.NavigateTo("forbidden");
        }
        return response;
    }
}
