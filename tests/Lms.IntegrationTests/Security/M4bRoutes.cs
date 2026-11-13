namespace Lms.IntegrationTests.Security;

/// <summary>
/// Which API routes belong to M4b (live sessions and SCORM / H5P packages). The M3 prefix <c>/api/class-courses</c> also carries M4b
/// routes (<c>live-sessions</c> and <c>packages</c> of a class course); <see cref="M3EndpointMatrixTests"/> leaves them out because a
/// Student legitimately reads them, and <see cref="M4bEndpointMatrixTests"/> owns them. Add the prefix or the class-course segment
/// of every new M4b module here (a route missing from both lists would still be caught by <c>M1EndpointMatrixTests</c>, which needs
/// a probe row for every mapped route).
/// </summary>
public static class M4bRoutes
{
    /// <summary>Top-level prefixes owned by M4b (the contract's route tables, Task 1 and Task 2).</summary>
    private static readonly string[] Prefixes =
    [
        "/api/live-sessions", "/api/packages", "/api/package-attempts", "/api/integrations/zoom", "/api/dev/zoom",
    ];

    /// <summary>Segments after <c>/api/class-courses/{id}/</c> that M4b owns.</summary>
    private static readonly string[] ClassCourseSegments = ["live-sessions", "packages"];

    /// <summary>
    /// The one anonymous M4b route: Zoom calls it (D54). It declares no permission by design and is protected by the signature, the
    /// timestamp window and the rate limit; its own rows (401 / 413 / 429 / 200 once) belong to the webhook task.
    /// </summary>
    public const string ZoomWebhook = "/api/integrations/zoom/webhook";

    public static bool Is(string pattern)
    {
        if (Prefixes.Any(p => pattern.Equals(p, StringComparison.OrdinalIgnoreCase)
                              || pattern.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var parts = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // api / class-courses / {id} / <segment> ...
        return parts.Length >= 4 && parts[0] == "api" && parts[1] == "class-courses"
               && ClassCourseSegments.Contains(parts[3], StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsAnonymousByDesign(string pattern) => pattern.Equals(ZoomWebhook, StringComparison.OrdinalIgnoreCase);
}
