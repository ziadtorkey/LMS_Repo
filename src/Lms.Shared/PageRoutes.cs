namespace Lms.Shared;

/// <summary>
/// Client page routes (the <c>@page</c> templates of <c>Lms.Client</c>), for code that links to a page: notification deep links,
/// emails, redirects. They are app-relative paths (a notification deep link must start with a single "/"), never API routes
/// (see <see cref="Routes"/>). <c>PageRoutesTests</c> pins every value to the <c>@page</c> the page declares, so a renamed page
/// fails a test instead of leaving a dead link.
/// </summary>
public static class PageRoutes
{
    public const string ChooseContext = "/choose-context";

    public static string Staff(Guid staffId) => $"/staff/{staffId}";

    public static string ClassCourseForum(Guid classCourseId) => $"/class-courses/{classCourseId}/forum";

    public static string ForumThread(Guid classCourseId, Guid threadId) => $"{ClassCourseForum(classCourseId)}/{threadId}";

    public static string ClassCourseAnnouncements(Guid classCourseId) => $"/class-courses/{classCourseId}/announcements";
}
