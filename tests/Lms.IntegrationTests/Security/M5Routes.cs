namespace Lms.IntegrationTests.Security;

/// <summary>
/// Which API routes belong to M5 (grading scales and gradebook, timetable, attendance, excuses; M5 plan #621, route table of the plan).
/// Several M5 routes hang under prefixes owned by earlier milestones (<c>/api/class-courses</c>, <c>/api/classes</c>, <c>/api/students</c>,
/// <c>/api/org-units</c>, <c>/api/academic-years</c>), so ownership is decided by the first path segment <i>or</i> by the segment after
/// the id. <see cref="M3EndpointMatrixTests"/> leaves M5 routes out (a Student or Guardian legitimately reads them) and
/// <see cref="M5EndpointMatrixTests"/> owns them. Add every new M5 prefix or segment here: a route missing from both lists would still be caught
/// by <c>M1EndpointMatrixTests</c>, which needs a probe row for every mapped route.
/// </summary>
public static class M5Routes
{
    /// <summary>Top-level prefixes owned by M5 (wave contracts 1 to 3).</summary>
    private static readonly string[] Prefixes =
    [
        "/api/grade-reports", "/api/timetable-slots", "/api/attendance", "/api/attendance-sessions", "/api/attendance-reports", "/api/excuses",
    ];

    /// <summary>
    /// Owned by M5 through the segment after the id: (<c>parent</c> after <c>/api</c>, segments after <c>{id}</c>), e.g.
    /// <c>/api/class-courses/{id}/gradebook</c> or <c>/api/students/{id}/grades</c>.
    /// </summary>
    private static readonly (string Parent, string[] Segments)[] ParentSegments =
    [
        ("class-courses", ["gradebook", "attendance-sessions"]),
        ("classes", ["timetable"]),
        ("org-units", ["grading-scales", "weight-templates", "periods"]),
        ("academic-years", ["term-structure"]),
        ("students", ["grades", "timetable", "attendance", "excuse-policy", "excusable-sessions"]),
    ];

    /// <summary>The caller's own M5 reads under <c>/api/me/</c>.</summary>
    private static readonly string[] MeSegments = ["grades", "timetable", "attendance", "excuses"];

    public static bool Is(string pattern)
    {
        if (Prefixes.Any(p => pattern.Equals(p, StringComparison.OrdinalIgnoreCase)
                              || pattern.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var parts = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && parts[0] == "api" && parts[1] == "me" && MeSegments.Contains(parts[2], StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        // api / <parent> / {id} / <segment> ...
        return parts.Length >= 4 && parts[0] == "api"
               && ParentSegments.Any(x => x.Parent == parts[1] && x.Segments.Contains(parts[3], StringComparer.OrdinalIgnoreCase));
    }
}
