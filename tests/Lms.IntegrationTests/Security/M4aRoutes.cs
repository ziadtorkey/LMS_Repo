using Lms.Shared;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Which API routes belong to M4a (teaching and learning). The M3 prefix <c>/api/class-courses</c> also carries M4a routes
/// (modules under a lesson, progress, activities of a class course); <see cref="M3EndpointMatrixTests"/> leaves them out because a
/// Student legitimately reads them, and <see cref="M4aEndpointMatrixTests"/> owns them. Add the prefix or the class-course
/// segment of every new M4a module here (a route missing from both lists would still be caught by
/// <c>M1EndpointMatrixTests</c>, which needs a probe row for every mapped route).
/// </summary>
public static class M4aRoutes
{
    /// <summary>
    /// Top-level prefixes owned by M4a. Not <c>/api/assignments</c> (M1 role assignments) and not the whole <c>/api/dev/jobs</c> (the
    /// M1 and M2 job triggers); revisit when the wave 2 contract (assignments, quizzes, library, forums) fixes the routes.
    /// </summary>
    private static readonly string[] Prefixes =
    [
        "/api/modules", "/api/assets", "/api/library", "/api/course-assignments", "/api/submissions", "/api/quizzes", "/api/quiz-attempts",
        "/api/forums", "/api/forum-threads", "/api/forum-posts", "/api/announcements", "/api/dev/jobs/quiz-close",
    ];

    /// <summary>Segments after <c>/api/class-courses/{id}/</c> that M4a owns (the M3 ones are teachers, students, groups, enrollments ...).</summary>
    private static readonly string[] ClassCourseSegments =
        ["lessons", "progress", "assignments", "quizzes", "forum", "forums", "announcements", "library", "modules"];

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
               && ClassCourseSegments.Contains(parts[3], StringComparer.OrdinalIgnoreCase)
               // lessons/{lessonKey}/modules is M4a; a bare lessons segment of the M3 lesson plan is M3 (GET .../lesson-plan has no such segment).
               && (parts[3] != "lessons" || parts.Contains("modules"));
    }
}
