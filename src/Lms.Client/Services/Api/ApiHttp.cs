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
        // An empty body (204, or chunked with no length) is not JSON.
        var text = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(text))
            return default;
        return JsonSerializer.Deserialize<TResponse>(text, JsonSerializerOptions.Web);
    }

    public async Task SendAsync(HttpMethod method, string route, object? body, CancellationToken ct = default)
    {
        using var response = await SendCoreAsync(method, route, body, ct);
    }

    /// <summary>Sends a multipart/raw body (file uploads) and reads the JSON response.</summary>
    public async Task<TResponse?> UploadAsync<TResponse>(string route, HttpContent content, CancellationToken ct = default)
    {
        using var response = await SendCoreAsync(HttpMethod.Post, route, null, ct, content);
        var text = await response.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<TResponse>(text, JsonSerializerOptions.Web);
    }

    public async Task<(Stream Content, string FileName)> DownloadAsync(string route, CancellationToken ct = default)
    {
        using var response = await SendCoreAsync(HttpMethod.Get, route, null, ct);
        var name = response.Content.Headers.ContentDisposition?.FileNameStar
                   ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                   ?? "download";
        var buffer = new MemoryStream();
        await (await response.Content.ReadAsStreamAsync(ct)).CopyToAsync(buffer, ct);
        buffer.Position = 0;
        return (buffer, name);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpMethod method, string route, object? body, CancellationToken ct, HttpContent? raw = null)
    {
        using var request = new HttpRequestMessage(method, route.TrimStart('/'));
        if (context.ActiveAssignmentId is { } id)
            request.Headers.Add(ContextHeader.Name, id.ToString());
        request.Headers.AcceptLanguage.ParseAdd(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        if (raw is not null)
            request.Content = raw;
        else if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType());

        var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return response;

        using (response)
            throw await ApiProblemException.ClassifyAsync(response, method, ct);
    }
}
