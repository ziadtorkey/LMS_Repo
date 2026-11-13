using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>A guardian's view of one child's published grades (M5 plan Task 29). Scaffold: update the test ids with the FE page.</summary>
public sealed class ChildGradesPage(IPage page)
{
    public ILocator Course(Guid classCourseId) => page.GetByTestId($"child-grades-course-{classCourseId}");

    public ILocator Items => page.GetByTestId("child-grades-item");

    public ILocator Final => page.GetByTestId("child-grades-final");
}
