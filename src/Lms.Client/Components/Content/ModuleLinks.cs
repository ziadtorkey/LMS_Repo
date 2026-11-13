namespace Lms.Client.Components.Content;

/// <summary>Link modules open only absolute http/https addresses (the server enforces the same rule).</summary>
public static class ModuleLinks
{
    public static bool IsAllowed(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
