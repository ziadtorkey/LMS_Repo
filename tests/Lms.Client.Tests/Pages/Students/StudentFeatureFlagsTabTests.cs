using System.Globalization;
using Bunit;
using Lms.Client.Pages.Students;
using Lms.Client.Tests.Support;
using Lms.Shared.Configuration;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Students;

public sealed class StudentFeatureFlagsTabTests : StudentsTestContext
{
    private const string Virtual = "Features.VirtualClasses";
    private const string Library = "Features.DigitalLibrary";

    private IRenderedComponent<StudentFeatureFlagsTab> RenderTab(bool canEdit = true)
    {
        Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
        return Render<StudentFeatureFlagsTab>(p => p.Add(t => t.Student, Students.All[0]).Add(t => t.CanEdit, canEdit));
    }

    [Fact]
    public async Task One_row_per_flag_showing_the_inherited_value()
    {
        Culture.Current = new CultureInfo("en");
        Settings.Values[Library] = "true";
        var cut = RenderTab();

        await cut.WaitForAssertionAsync(() => Assert.Contains("Inherit (On)", StringSelect(cut, $"flag-{Library}").Markup));
        Assert.Equal(FeatureFlags.All.Count, cut.FindComponents<MudSelect<string>>().Count);
        Assert.Contains("Inherit (Off)", StringSelect(cut, $"flag-{Virtual}").Markup);
    }

    [Fact]
    public async Task Feature_flag_inherit_sends_null_and_explicit_values_send_booleans()
    {
        var cut = RenderTab();
        var library = StringSelect(cut, $"flag-{Library}");
        var virtualClasses = StringSelect(cut, $"flag-{Virtual}");
        await cut.InvokeAsync(() => library.Instance.ValueChanged.InvokeAsync("on"));
        await cut.InvokeAsync(() => virtualClasses.Instance.ValueChanged.InvokeAsync("off"));

        await cut.Find("[data-testid=flags-save]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(Students.FlagUpdates));
        var overrides = Students.FlagUpdates[0].Request.Overrides;
        Assert.True(overrides[Library]);
        Assert.False(overrides[Virtual]);
        Assert.Null(overrides[FeatureFlags.Messaging.Name]);

        // Going back to Inherit clears the override.
        await cut.InvokeAsync(() => library.Instance.ValueChanged.InvokeAsync("inherit"));
        await cut.Find("[data-testid=flags-save]").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => Assert.Equal(2, Students.FlagUpdates.Count));
        Assert.Null(Students.FlagUpdates[1].Request.Overrides[Library]);
    }

    [Fact]
    public async Task Existing_overrides_are_preselected_and_view_only_has_no_save()
    {
        var student = Students.All[0];
        Students.FlagUpdates.Clear();
        Culture.Current = new CultureInfo("en");
        Render<MudPopoverProvider>();
        var cut = Render<StudentFeatureFlagsTab>(p => p
            .Add(t => t.Student, student with { FeatureFlagOverrides = new Dictionary<string, bool> { [Library] = true } })
            .Add(t => t.CanEdit, false));

        Assert.Contains("On", StringSelect(cut, $"flag-{Library}").Markup);
        Assert.Empty(cut.FindAll("[data-testid=flags-save]"));
        await Task.CompletedTask;
    }
}
