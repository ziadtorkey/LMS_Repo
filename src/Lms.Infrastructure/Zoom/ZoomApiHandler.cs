using System.Net;
using System.Net.Http.Headers;

namespace Lms.Infrastructure.Zoom;

/// <summary>
/// Outgoing pipeline for the Zoom API client: bearer token from <see cref="ZoomTokenCache"/>, the outbound limiter, one retry after
/// a short <c>429 Retry-After</c> (a longer one is reported as <see cref="ZoomFailure.RateLimited"/> without waiting) and one retry
/// with a fresh token after a 401. Every other status goes back to the caller untouched.
/// </summary>
public sealed class ZoomApiHandler(ZoomTokenCache tokens, ZoomOutboundLimiter limiter) : DelegatingHandler
{
    /// <summary>Longest <c>Retry-After</c> a request thread waits out; beyond it the call fails and the sweep retries later.</summary>
    public static readonly TimeSpan MaxInlineWait = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(1);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var rateClass = ZoomOutboundLimiter.Classify(request.Method, request.RequestUri?.AbsolutePath ?? "");
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = request.Content?.Headers.ContentType;
        var refreshed = false;
        var waited = false;

        while (true)
        {
            await limiter.AcquireAsync(rateClass, cancellationToken);
            var token = await tokens.GetTokenAsync(cancellationToken);
            using var attempt = Clone(request, body, contentType, token);

            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(attempt, cancellationToken);
            }
            catch (HttpRequestException)
            {
                throw new ZoomProviderException(ZoomFailure.Unavailable, "Zoom API unreachable.");
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
            {
                refreshed = true;
                tokens.Invalidate(token);
                response.Dispose();
                continue;
            }

            if (response.StatusCode == (HttpStatusCode)429)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? DefaultRetryAfter;
                response.Dispose();
                limiter.Penalize(rateClass, retryAfter);
                if (waited || retryAfter > MaxInlineWait)
                {
                    throw new ZoomProviderException(ZoomFailure.RateLimited, "Zoom rate limit reached.", retryAfter);
                }

                waited = true;
                continue;
            }

            return response;
        }
    }

    private static HttpRequestMessage Clone(HttpRequestMessage source, byte[]? body, MediaTypeHeaderValue? contentType, string token)
    {
        var copy = new HttpRequestMessage(source.Method, source.RequestUri) { Version = source.Version };
        foreach (var header in source.Headers)
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        copy.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            copy.Content = new ByteArrayContent(body);
            copy.Content.Headers.ContentType = contentType;
        }

        return copy;
    }
}
