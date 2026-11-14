using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.Pages;

/// <summary>
/// The student's own published grades (<c>/me/grades</c>, M5 Task 29, #662); the guardian's view of a child (<c>/me/children/{id}/grades</c>) is the same
/// component, so one page object serves both. Ids are the ones of #662.
/// </summary>
public sealed class MyGradesPage(IPage page)
{
    public ILocator Title => page.GetByTestId("grades-title");

    public ILocator StudentName => page.GetByTestId("grades-student");

    public ILocator Courses => page.GetByTestId("grades-courses");

    public ILocator Empty(Guid classCourseId) => page.GetByTestId($"grades-empty-{classCourseId}");

    public ILocator Course(Guid classCourseId) => page.GetByTestId($"grades-course-{classCourseId}");

    public ILocator Item(Guid itemId) => page.GetByTestId($"grades-item-{itemId}");

    public ILocator Percent(Guid classCourseId) => page.GetByTestId($"grades-percent-{classCourseId}");

    public ILocator Basis(Guid classCourseId) => page.GetByTestId($"grades-basis-{classCourseId}");

    public ILocator Band(Guid classCourseId) => page.GetByTestId($"grades-band-{classCourseId}");

    public ILocator Hint(Guid classCourseId) => page.GetByTestId($"grades-hint-{classCourseId}");

    public ILocator Legend(Guid classCourseId) => page.GetByTestId($"grades-legend-{classCourseId}");

    public ILocator ChildSelect => page.GetByTestId("grades-child-select");

    public async Task OpenAsync() => await OpenAtAsync("/me/grades");

    /// <summary>A guardian opens one child's grades.</summary>
    public async Task OpenChildAsync(Guid studentId) => await OpenAtAsync($"/me/children/{studentId}/grades");

    private async Task OpenAtAsync(string url)
    {
        await page.GotoAsync(url);
        await Expect(Title).ToBeVisibleAsync();
    }
}
