using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>A student's course home (/class-courses/{id}/learn): progress, the continue card and the visible modules by lesson.</summary>
public sealed class MyCoursePage(IPage page)
{
    public ILocator ProgressText => page.GetByTestId("learn-progress-text");
    public ILocator ContinueTitle => page.GetByTestId("continue-title");
    public ILocator ContinueOpen => page.GetByTestId("continue-open");
    public ILocator ContinueDone => page.GetByTestId("continue-done");
    public ILocator Header => page.GetByTestId("learn-header");

    /// <summary>The titles of every module the student sees, in page order.</summary>
    public ILocator ModuleLinks => page.Locator("[data-testid^='learn-module-link-']");

    public ILocator Module(Guid moduleId) => page.GetByTestId($"learn-module-{moduleId}");

    public ILocator ModuleLink(Guid moduleId) => page.GetByTestId($"learn-module-link-{moduleId}");

    public ILocator ModuleState(Guid moduleId) => page.GetByTestId($"learn-module-state-{moduleId}");

    public async Task OpenAsync(Guid classCourseId)
    {
        await page.GotoAsync($"/class-courses/{classCourseId}/learn");
        await Header.WaitForAsync();
    }
}

/// <summary>The module viewer (/class-courses/{id}/modules/{moduleId}).</summary>
public sealed class ModuleViewerPage(IPage page)
{
    public ILocator Title => page.GetByTestId("module-title");
    public ILocator MarkComplete => page.GetByTestId("module-mark-complete");
    public ILocator Done => page.GetByTestId("module-done");
    public ILocator Next => page.GetByTestId("module-next");
    public ILocator Previous => page.GetByTestId("module-prev");
    public ILocator Back => page.GetByTestId("module-back");
    public ILocator Forbidden => page.GetByTestId("module-forbidden");
    public ILocator LoadError => page.GetByTestId("module-load-error");
    public ILocator Link => page.GetByTestId("module-link");

    public async Task OpenAsync(Guid classCourseId, Guid moduleId) =>
        await page.GotoAsync($"/class-courses/{classCourseId}/modules/{moduleId}");
}
