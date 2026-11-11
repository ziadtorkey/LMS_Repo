using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lms.E2ETests.Infrastructure;

/// <summary>
/// Reads the emails the API sent in Development (Mailpit on :8025, SMTP :1025 in docker compose) and extracts the
/// account link (invitation or password reset).
/// </summary>
public sealed partial class MailpitClient(Uri? baseUrl = null) : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http = new()
    {
        BaseAddress = baseUrl ?? new Uri(Environment.GetEnvironmentVariable("LMS_E2E_MAILPIT") ?? "http://localhost:8025"),
    };

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// Waits for the newest email to <paramref name="email"/> and returns the path + query of its account link
    /// (<c>/Account/ResetPassword?userId=…&amp;token=…</c>). Only the path and query are returned: the link's host is
    /// the configured public base URL, which may differ from where the E2E stack runs.
    /// </summary>
    public async Task<string> WaitForAccountLinkAsync(string email)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            var id = await NewestMessageIdAsync(email);
            if (id is not null)
            {
                var message = await _http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{id}");
                var text = message.GetProperty("Text").GetString() ?? string.Empty;
                var match = AccountLink().Match(text);
                if (match.Success)
                {
                    var uri = new Uri(match.Value);
                    return uri.PathAndQuery;
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"No account email with a reset/invitation link reached {email} within {Timeout.TotalSeconds}s (Mailpit {_http.BaseAddress}).");
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>How many emails have reached <paramref name="email"/> so far (no waiting).</summary>
    public async Task<int> CountAsync(string email)
    {
        var query = Uri.EscapeDataString($"to:\"{email}\"");
        var result = await _http.GetFromJsonAsync<JsonElement>($"/api/v1/search?query={query}");
        return result.TryGetProperty("messages", out var messages) ? messages.GetArrayLength() : 0;
    }

    private async Task<string?> NewestMessageIdAsync(string email)
    {
        var query = Uri.EscapeDataString($"to:\"{email}\"");
        var result = await _http.GetFromJsonAsync<JsonElement>($"/api/v1/search?query={query}");
        return result.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0
            ? messages[0].GetProperty("ID").GetString()
            : null;
    }

    [GeneratedRegex(@"https?://[^\s""<>]+/Account/ResetPassword\?[^\s""<>]+")]
    private static partial Regex AccountLink();
}
