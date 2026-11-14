using System.Text.Json;

namespace Lms.Infrastructure.Packages;

/// <summary>
/// D56 / Decision 10: enabling the <c>ScormContent</c> flag for a unit is refused (409 <c>package_host_not_configured</c>) while the
/// content host is not a separate registrable domain, the same pattern as the <c>VirtualClasses</c> guard. Turning the flag off is always allowed.
/// </summary>
public static class ScormContentEnableGuard
{
    /// <summary>True when setting the flag to <paramref name="valueJson"/> must be refused.</summary>
    public static bool Refuses(string? valueJson, PackageHostOptions options)
    {
        return !options.IsConfigured && IsTrue(valueJson);
    }

    private static bool IsTrue(string? valueJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(valueJson ?? string.Empty);
            return doc.RootElement.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
