using System.Net;

namespace Lms.Infrastructure.Comms.Push;

/// <summary>
/// Which push-service endpoints the server may call (M6 Task 8, SSRF guard). The endpoint is typed by the user, and the server later POSTs to it with a VAPID
/// header, so only https URLs on the default port whose host is a real push service are accepted: no IP literal, no <c>localhost</c>, no user info, and the
/// host must equal an allowed suffix or end with it after a dot (so <c>fcm.googleapis.com.evil.example</c> fails). The same check runs again at send time.
/// </summary>
public static class PushEndpointPolicy
{
    public static IReadOnlyList<string> DefaultHostSuffixes { get; } =
        ["fcm.googleapis.com", "push.services.mozilla.com", "push.apple.com", "notify.windows.com"];

    public static bool IsAllowed(string? endpoint, IReadOnlyCollection<string>? suffixes = null)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.HostNameType != UriHostNameType.Dns || IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out _))
        {
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal))
        {
            return false;
        }

        var allowed = suffixes is { Count: > 0 } ? suffixes : DefaultHostSuffixes;
        return allowed.Any(s => host == s.Trim('.').ToLowerInvariant() || host.EndsWith("." + s.Trim('.').ToLowerInvariant(), StringComparison.Ordinal));
    }

    /// <summary>The HTTP handler of the push client: a redirect is never followed, so an allowed host cannot send the server on to another one.</summary>
    public static SocketsHttpHandler CreateHandler() => new() { AllowAutoRedirect = false };
}
