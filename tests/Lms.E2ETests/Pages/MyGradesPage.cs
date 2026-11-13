using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The student's own published grades (M5 plan Task 29). Scaffold: update the test ids with the FE page.</summary>
public sealed class MyGradesPage(IPage page)
{
    public ILocator Course(Guid classCourseId) => page.GetByTestId($"grades-course-{classCourseId}");

    public ILocator Items => page.GetByTestId("grades-item");

    public ILocator Final => page.GetByTestId("grades-final");

    public ILocator Band => page.GetByTestId("grades-band");

    public ILocator Legend => page.GetByTestId("grades-legend");
}
