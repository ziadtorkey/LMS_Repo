using Lms.Domain.Content;
using Lms.Shared.Content;

namespace Lms.UnitTests.Content;

public sealed class ModuleConstantsTests
{
    [Fact]
    public void Domain_constants_match_the_contract_catalogs()
    {
        Assert.Equal(ModuleVisibilities.Published, LessonModule.PublishedVisibility);
        Assert.Equal(ModuleVisibilities.Scheduled, LessonModule.ScheduledVisibility);
        Assert.Equal(ModuleTargets.ClassCourse, LessonModule.ClassCourseTarget);
        Assert.Equal(CompletionRules.None, LessonModule.NoCompletion);
        Assert.Equal(CompletionStates.NotStarted, ModuleCompletion.NotStartedState);
        Assert.Equal(CompletionStates.InProgress, ModuleCompletion.InProgressState);
        Assert.Equal(CompletionStates.Completed, ModuleCompletion.CompletedState);
    }
}
