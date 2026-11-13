using System.Net;
using System.Text;
using Lms.Domain.Abstractions;

namespace Lms.UnitTests.Zoom;

/// <summary>A clock tests move by hand.</summary>
internal sealed class ManualClock(DateTime start) : IClock
{
    public DateTime UtcNow { get; set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>A stand-in for the Zoom HTTP API: answers from a function and records every request.</summary>
internal sealed class FakeZoomHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private int _calls;

    public List<(HttpMethod Method, string Url, string? Authorization, string? Body)> Requests { get; } = [];

    public int Calls => _calls;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests)
        {
            Requests.Add((request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
        }

        return await respond(request, Interlocked.Increment(ref _calls));
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Token(string token, int expiresIn = 3600) =>
        Json(HttpStatusCode.OK, $$"""{"access_token":"{{token}}","token_type":"bearer","expires_in":{{expiresIn}}}""");
}
