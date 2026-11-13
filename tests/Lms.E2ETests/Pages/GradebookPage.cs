using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The teacher's gradebook grid on the class course (M5 plan Tasks 24-26). Scaffold: test ids follow the FE page when it lands; update them here once.</summary>
public sealed class GradebookPage(IPage page)
{
    public ILocator Grid => page.GetByTestId("gradebook-grid");

    public ILocator WeightsChip => page.GetByTestId("gradebook-weights");

    public ILocator PublishButton => page.GetByTestId("gradebook-publish");

    public ILocator Cell(Guid studentId, Guid itemId) => page.GetByTestId($"grade-cell-{studentId}-{itemId}");
}
