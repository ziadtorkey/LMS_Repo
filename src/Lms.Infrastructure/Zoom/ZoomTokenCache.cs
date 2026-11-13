using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lms.Domain.Abstractions;
using Microsoft.Extensions.Options;

namespace Lms.Infrastructure.Zoom;

/// <summary>
/// Server-to-Server OAuth (<c>account_credentials</c> grant). One token is cached until shortly before it expires; concurrent
/// callers share one refresh in flight.
/// </summary>
public sealed class ZoomTokenCache(HttpClient http, IOptions<ZoomOptions> options, IClock clock)
{
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly object _state = new();
    private string? _token;
    private DateTime _expiresAtUtc;

    public async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        if (Current() is { } cached)
        {
            return cached;
        }

        await _refresh.WaitAsync(ct);
        try
        {
            return Current() ?? await RequestAsync(ct);
        }
        finally
        {
            _refresh.Release();
        }
    }

    /// <summary>Drops the cached token when it is still <paramref name="rejected"/> (Zoom answered 401), so the next call fetches a new one.</summary>
    public void Invalidate(string rejected)
    {
        lock (_state)
        {
            if (_token == rejected)
            {
                _token = null;
            }
        }
    }

    private string? Current()
    {
        lock (_state)
        {
            return _token is not null && clock.UtcNow < _expiresAtUtc - Margin ? _token : null;
        }
    }

    private async Task<string> RequestAsync(CancellationToken ct)
    {
        var zoom = options.Value;
        var url = $"{zoom.TokenUrl}?grant_type=account_credentials&account_id={Uri.EscapeDataString(zoom.AccountId)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(zoom.ClientId + ":" + zoom.ClientSecret)));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException)
        {
            throw new ZoomProviderException(ZoomFailure.Unavailable, "Zoom token endpoint unreachable.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // The body is deliberately not read into the message: it can repeat the credentials that were sent.
                var failure = (int)response.StatusCode is 400 or 401 or 403 ? ZoomFailure.Unauthorized : ZoomFailure.Unavailable;
                throw new ZoomProviderException(failure, $"Zoom token request failed with status {(int)response.StatusCode}.");
            }

            string token;
            int expiresIn;
            try
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                token = json.RootElement.GetProperty("access_token").GetString() ?? throw new JsonException();
                expiresIn = json.RootElement.GetProperty("expires_in").GetInt32();
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new ZoomProviderException(ZoomFailure.Unavailable, "Zoom token response was not understood.");
            }

            lock (_state)
            {
                _token = token;
                _expiresAtUtc = clock.UtcNow.AddSeconds(expiresIn);
            }

            return token;
        }
    }
}
