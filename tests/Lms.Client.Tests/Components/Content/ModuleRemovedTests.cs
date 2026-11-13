using System.Globalization;
using Bunit;
using Lms.Client.Components.Content;
using Lms.Client.Tests.Support;
using Lms.Shared.Content;

namespace Lms.Client.Tests.Components.Content;

/// <summary>A module whose activity was deleted (RefRemoved, #526) shows a muted "Removed" state in the plan tree and cannot be opened.</summary>
public sealed class ModuleRemovedTests : ClientTestContext
{
    private static ModuleNodeDto Node(bool removed, string kind = ModuleKinds.Quiz) =>
        new(Guid.NewGuid(), kind, 1, "اختبار", "Quiz one", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, null, null,
            CompletionRules.Score, true, CompletionStates.Completed, RefRemoved: removed);

    private IRenderedComponent<ModuleNode> Render(ModuleNodeDto node, bool staff, string culture = "en")
    {
        Culture.Current = new CultureInfo(culture);
        return Render<ModuleNode>(p => p.Add(x => x.Node, node).Add(x => x.Staff, staff));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_removed_module_has_no_link_and_shows_the_removed_caption(bool staff)
    {
        var cut = Render(Node(removed: true), staff);

        Assert.Empty(cut.FindAll("[data-testid=module-title]"));
        Assert.Empty(cut.FindAll("a"));
        Assert.Contains("Quiz one", cut.Find("[data-testid=module-title-removed]").TextContent);
        Assert.Equal("Removed", cut.Find("[data-testid=module-removed]").TextContent.Trim());
        Assert.Contains("lms-module-removed", cut.Find("[data-testid^=module-]").ClassName);
    }

    [Fact]
    public void A_removed_module_tells_screen_readers_why_and_hides_the_completion_state()
    {
        var cut = Render(Node(removed: true), staff: false);

        var sr = cut.Find("[data-testid=module-removed-sr]");
        Assert.Contains("lms-visually-hidden", sr.ClassName);
        Assert.Contains("no longer be opened", sr.TextContent);
        Assert.Empty(cut.FindAll("[data-testid=module-state]"));   // a completion state on a deleted activity would be misleading
    }

    [Fact]
    public void A_module_that_is_not_removed_is_unchanged()
    {
        var cut = Render(Node(removed: false), staff: false);

        Assert.NotEmpty(cut.FindAll("[data-testid=module-title]"));
        Assert.Empty(cut.FindAll("[data-testid=module-removed]"));
        Assert.NotEmpty(cut.FindAll("[data-testid=module-state]"));
    }

    [Fact]
    public void The_removed_state_is_in_arabic_too()
    {
        var cut = Render(Node(removed: true), staff: false, culture: "ar");

        Assert.Equal("محذوف", cut.Find("[data-testid=module-removed]").TextContent.Trim());
        Assert.DoesNotContain("Module_Removed", cut.Markup);
    }
}
