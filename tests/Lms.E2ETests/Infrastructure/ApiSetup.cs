using System.Text.Json;
using Microsoft.Playwright;

namespace Lms.E2ETests.Infrastructure;

/// <summary>
/// Set-up through the API for everything a scenario is not testing (courses, allocations, classes, teachers): an
/// authenticated <c>fetch</c> from the signed-in page, with the access token the Blazor client keeps in session
/// storage and the active context from local storage. The feature under test is still driven through the UI.
/// </summary>
public sealed class ApiSetup(IPage page)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<JsonElement> GetAsync(string url) => SendAsync("GET", url, null);

    public Task<JsonElement> PostAsync(string url, object body) => SendAsync("POST", url, body);

    public Task<JsonElement> PutAsync(string url, object body) => SendAsync("PUT", url, body);

    /// <summary>The id of the org unit with this code (units are listed with their codes).</summary>
    public async Task<Guid> UnitIdByCodeAsync(string code)
    {
        var units = await GetAsync("/api/org-units");
        foreach (var unit in units.EnumerateArray())
        {
            if (unit.GetProperty("code").GetString() == code)
            {
                return unit.GetProperty("id").GetGuid();
            }
        }

        throw new InvalidOperationException($"No org unit with code '{code}'.");
    }

    private async Task<JsonElement> SendAsync(string method, string url, object? body)
    {
        var payload = body is null ? null : JsonSerializer.Serialize(body, Json);
        var result = await page.EvaluateAsync<string>(
            """
            async ([method, url, payload]) => {
              const key = Object.keys(sessionStorage).find(x => x.startsWith('oidc.user:'));
              const token = JSON.parse(sessionStorage.getItem(key)).access_token;
              const headers = { Authorization: 'Bearer ' + token, 'X-Lms-Context': localStorage.getItem('lms.context') ?? '' };
              if (payload !== null) headers['Content-Type'] = 'application/json';
              const r = await fetch(url, { method, headers, body: payload });
              return JSON.stringify({ status: r.status, text: await r.text() });
            }
            """,
            new object?[] { method, url, payload });
        using var document = JsonDocument.Parse(result);
        var status = document.RootElement.GetProperty("status").GetInt32();
        var text = document.RootElement.GetProperty("text").GetString() ?? string.Empty;
        if (status is < 200 or >= 300)
        {
            throw new InvalidOperationException($"{method} {url} answered {status}: {text}");
        }

        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }
}
