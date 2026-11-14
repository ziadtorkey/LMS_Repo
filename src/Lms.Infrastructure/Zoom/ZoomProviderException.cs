namespace Lms.Infrastructure.Zoom;

public enum ZoomFailure
{
    /// <summary>The app credentials were refused.</summary>
    Unauthorized,

    /// <summary>Zoom is down, unreachable or answered something unusable.</summary>
    Unavailable,

    /// <summary>Zoom (or our own outbound limiter) said to slow down.</summary>
    RateLimited,
}

/// <summary>
/// A failed Zoom call. The message never carries credentials, tokens or response bodies (those can echo a secret); callers map
/// it to <c>provider_unavailable</c> and may use <see cref="RetryAfter"/> for the <c>Retry-After</c> header.
/// </summary>
public sealed class ZoomProviderException(ZoomFailure failure, string message, TimeSpan? retryAfter = null) : Exception(message)
{
    public ZoomFailure Failure { get; } = failure;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}
