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

    /// <summary>Like <see cref="SendAsync{TResponse}"/> but also returns the value of one response header (null when absent).</summary>
    public async Task<(TResponse? Value, string? Header)> SendWithHeaderAsync<TResponse>(
        HttpMethod method, string route, object? body, string headerName, CancellationToken ct = default)
    {
        using var response = await SendCoreAsync(method, route, body, ct);
        var header = response.Headers.TryGetValues(headerName, out var values) ? values.FirstOrDefault() : null;
        var text = await response.Content.ReadAsStringAsync(ct);
        var value = response.StatusCode == HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(text)
            ? default
            : JsonSerializer.Deserialize<TResponse>(text, JsonSerializerOptions.Web);
        return (value, header);
    }

    public async Task SendAsync(HttpMethod method, string route, object? body, CancellationToken ct = default)
    {
        using var response = await SendCoreAsync(method, route, body, ct);
    }

    /// <summary>Uploads one file as multipart/form-data (field "file") and returns the JSON response.</summary>
    public async Task<TResponse> UploadAsync<TResponse>(string route, string fileName, string contentType, Stream content, IReadOnlyDictionary<string, string>? fields = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route.TrimStart('/'));
        if (context.ActiveAssignmentId is { } id)
            request.Headers.Add(ContextHeader.Name, id.ToString());
        request.Headers.AcceptLanguage.ParseAdd(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        var file = new StreamContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        foreach (var (name, value) in fields ?? new Dictionary<string, string>())
            form.Add(new StringContent(value), name);
        request.Content = form;

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw await ApiProblemException.ClassifyAsync(response, HttpMethod.Post, ct);
        return JsonSerializer.Deserialize<TResponse>(await response.Content.ReadAsStringAsync(ct), JsonSerializerOptions.Web)!;
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
            throw await ApiProblemException.ClassifyAsync(response, method, ct);
    }
}
