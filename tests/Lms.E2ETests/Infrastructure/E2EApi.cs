using System.Text.Json;
using Microsoft.Playwright;

namespace Lms.E2ETests.Infrastructure;

/// <summary>
/// Calls the API the way the signed-in browser does: with the access token the Blazor client keeps in sessionStorage and
/// the active context from localStorage. Used for set-up and clean-up that has no UI yet, and to ask the API what the
/// session may see (so a test can prove a limit the UI also enforces).
/// </summary>
public sealed class E2EApi(IPage page, Uri baseUrl)
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>The bearer token of the page's session (the OIDC user the WASM authentication library stored).</summary>
    public async Task<string> TokenAsync()
    {
        var token = await page.EvaluateAsync<string?>(
            "() => { for (const k of Object.keys(sessionStorage)) { if (k.startsWith('oidc.user:')) { return JSON.parse(sessionStorage.getItem(k)).access_token; } } return null; }");
        return token ?? throw new InvalidOperationException("No OIDC user in sessionStorage: is the page signed in and the app loaded?");
    }

    /// <summary>The active assignment (context) id the app stored for this session, if any.</summary>
    public Task<string?> ActiveContextAsync() => page.EvaluateAsync<string?>("() => localStorage.getItem('lms.context')");

    /// <summary>Sends a multipart form (file uploads) as the signed-in session.</summary>
    public async Task<IAPIResponse> SendFormAsync(string method, string path, IReadOnlyDictionary<string, string> fields, string fileField, FilePayload file)
    {
        var form = page.Context.APIRequest.CreateFormData();
        foreach (var (key, value) in fields)
        {
            form.Set(key, value);
        }

        form.Set(fileField, file);
        var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + await TokenAsync() };
        var context = await ActiveContextAsync() ?? await SoleContextAsync(headers);
        if (!string.IsNullOrEmpty(context))
        {
            headers["X-Lms-Context"] = context;
        }

        return await page.Context.APIRequest.FetchAsync(new Uri(baseUrl, path).ToString(), new APIRequestContextOptions
        {
            Method = method,
            Headers = headers,
            Multipart = form,
            IgnoreHTTPSErrors = true,
        });
    }

    /// <param name="contextId">Context header to send: null = the page's active context, Guid.Empty = none.</param>
    public async Task<IAPIResponse> SendAsync(string method, string path, object? body = null, Guid? contextId = null)
    {
        var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + await TokenAsync() };
        var context = contextId is { } id ? (id == Guid.Empty ? null : id.ToString()) : await ActiveContextAsync() ?? await SoleContextAsync(headers);
        if (!string.IsNullOrEmpty(context))
        {
            headers["X-Lms-Context"] = context;
        }

        return await page.Context.APIRequest.FetchAsync(new Uri(baseUrl, path).ToString(), new APIRequestContextOptions
        {
            Method = method,
            Headers = headers,
            DataObject = body,
            IgnoreHTTPSErrors = true,
        });
    }

    /// <summary>
    /// A person with a single context gets it selected without the app storing it, so ask the API (no context header):
    /// when there is exactly one, that is the active one.
    /// </summary>
    private async Task<string?> SoleContextAsync(Dictionary<string, string> headers)
    {
        var me = await page.Context.APIRequest.FetchAsync(new Uri(baseUrl, "/api/me").ToString(),
            new APIRequestContextOptions { Method = "GET", Headers = headers, IgnoreHTTPSErrors = true });
        if (!me.Ok)
        {
            return null;
        }

        var contexts = JsonDocument.Parse(await me.TextAsync()).RootElement.GetProperty("contexts").EnumerateArray().ToList();
        return contexts.Count == 1 ? contexts[0].GetProperty("assignmentId").GetGuid().ToString() : null;
    }

    public async Task<JsonElement> JsonAsync(IAPIResponse response)
    {
        var text = await response.TextAsync();
        if (!response.Ok)
        {
            throw new InvalidOperationException($"{response.Status} {response.Url}: {text}");
        }

        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
