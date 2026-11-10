using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Shared.Authorization;

namespace Lms.Client.Services.Api;

/// <summary>Shared HTTP plumbing for every module API: context header, language, JSON and problem mapping.</summary>
public sealed class ApiHttp(HttpClient http, IContextStore context)
{
    public async Task<T> GetAsync<T>(string route, CancellationToken ct = default) =>
        (await SendAsync<T>(HttpMethod.Get, route, null, ct))!;

    public async Task<TResponse?> SendAsync<TResponse>(HttpMethod method, string route, object? body, CancellationToken ct = default)
    {
        using var response = await SendCoreAsync(method, route, body, ct);
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            return default;
        return await response.Content.ReadFromJsonAsync<TResponse>(ct);
    }

    public async Task SendAsync(HttpMethod method, string route, object? body, CancellationToken ct = default)
    {
        using var response = await SendCoreAsync(method, route, body, ct);
    }

    public async Task<(Stream Content, string FileName)> DownloadAsync(string route, CancellationToken ct = default)
    {
        var response = await SendCoreAsync(HttpMethod.Get, route, null, ct);
        var name = response.Content.Headers.ContentDisposition?.FileNameStar
                   ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                   ?? "download";
        var buffer = new MemoryStream();
        await (await response.Content.ReadAsStreamAsync(ct)).CopyToAsync(buffer, ct);
        response.Dispose();
        buffer.Position = 0;
        return (buffer, name);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string route, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, route.TrimStart('/'));
        if (context.ActiveAssignmentId is { } id)
            request.Headers.Add(ContextHeader.Name, id.ToString());
        request.Headers.AcceptLanguage.ParseAdd(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType());

        var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return response;

        using (response)
        {
            // A forbidden GET means the whole page is off limits (the redirect handler sends the user to /forbidden);
            // a forbidden action keeps the page and shows the problem.
            if (response.StatusCode == HttpStatusCode.Forbidden && method == HttpMethod.Get)
                throw new ApiForbiddenException();
            throw await ReadProblemAsync(response, ct);
        }
    }

    private static async Task<ApiProblemException> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? code = null, detail = null;
        Dictionary<string, string[]>? errors = null;
        try
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (json.ValueKind == JsonValueKind.Object)
            {
                code = Text(json, "code") ?? Text(json, "reason");
                detail = Text(json, "detail");
                if (json.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
                    errors = e.EnumerateObject().ToDictionary(
                        p => p.Name,
                        p => p.Value.ValueKind == JsonValueKind.Array
                            ? p.Value.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                            : []);
            }
        }
        catch (JsonException) { }
        catch (NotSupportedException) { }
        return new ApiProblemException((int)response.StatusCode, code, detail, errors);
    }

    private static string? Text(JsonElement json, string name) =>
        json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
