using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Lms.Client.Services.Api;

/// <summary>
/// An RFC 7807 error answered by the API. <see cref="Code"/> is the ProblemCodes value
/// (or the access-denial reason, e.g. BlockedByPolicy); <see cref="Detail"/> is server-localized text.
/// </summary>
public sealed class ApiProblemException(int status, string? code, string? detail, IReadOnlyDictionary<string, string[]>? errors, string? field = null)
    : HttpRequestException($"API error {status} {code}", null, (HttpStatusCode)status)
{
    public const string InvalidContext = "invalid_context";
    public const string ContextRequired = "context_required";

    public int Status { get; } = status;
    public string? Code { get; } = code;
    public string? Detail { get; } = detail;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;

    /// <summary>The problem's <c>field</c> extension (e.g. which unique field of a person collides), when present.</summary>
    public string? Field { get; } = field;

    /// <summary>True when the API says the active context is missing, expired, deleted or deactivated (bug #84, D15).</summary>
    public bool IsContextProblem => Status == 403 && Code is InvalidContext or ContextRequired;

    /// <summary>Reads the ProblemDetails body of a failed response (the content may be empty or not JSON).</summary>
    public static async Task<ApiProblemException> FromResponseAsync(HttpResponseMessage response, CancellationToken ct = default)
    {
        string? code = null, detail = null, field = null;
        Dictionary<string, string[]>? errors = null;
        try
        {
            // Read as a string, not as a JSON stream: ReadFromJsonAsync disposes the content stream, and the redirect
            // handler and the classifier both read the same body (bug #132). A buffered content can be read as a string repeatedly.
            var text = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(text);
            var json = document.RootElement;
            if (json.ValueKind == JsonValueKind.Object)
            {
                code = Text(json, "code") ?? Text(json, "reason");
                detail = Text(json, "detail");
                field = Text(json, "field");
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
        catch (InvalidOperationException) { }
        return new ApiProblemException((int)response.StatusCode, code, detail, errors, field);
    }

    /// <summary>
    /// The exception callers should see for a failed response: a stale/missing context, a forbidden GET
    /// (whole page off limits) or the problem itself (a forbidden action keeps the page).
    /// </summary>
    public static async Task<HttpRequestException> ClassifyAsync(HttpResponseMessage response, HttpMethod method, CancellationToken ct = default)
    {
        var problem = await FromResponseAsync(response, ct);
        if (problem.IsContextProblem) return new ApiContextException(problem.Code!);
        if (problem.Status == 403 && method == HttpMethod.Get) return new ApiForbiddenException();
        return problem;
    }

    private static string? Text(JsonElement json, string name) =>
        json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>The active context is missing or no longer valid: the user must pick a context again, not see Forbidden.</summary>
public sealed class ApiContextException(string code) : HttpRequestException($"Context problem: {code}", null, HttpStatusCode.Forbidden)
{
    public string Code { get; } = code;
}
