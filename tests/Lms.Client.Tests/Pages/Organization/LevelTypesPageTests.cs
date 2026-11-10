using System.Globalization;
using Bunit;
using Lms.Client.Pages.Organization;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Organization;

public sealed class LevelTypesPageTests : ClientTestContext
{
    private readonly LevelTypeDto _group = new(Guid.NewGuid(), "المجموعة", "Group", 1, false, 1);
    private readonly LevelTypeDto _school = new(Guid.NewGuid(), "المدرسة", "School", 2, false, 3);
    private readonly LevelTypeDto _unused = new(Guid.NewGuid(), "الصف", "Grade", 3, true, 0);

    private async Task<(IRenderedComponent<LevelTypesPage> Page, IRenderedComponent<MudDialogProvider> Dialogs)> RenderPageAsync(params string[] permissions)
    {
        Org.Levels.AddRange([_group, _school, _unused]);
        await GrantAsync(permissions);
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var page = Render<LevelTypesPage>();
        page.WaitForElement($"[data-testid=level-row-{_group.Id}]");
        return (page, dialogs);
    }

    [Fact]
    public async Task Level_types_reorder_sends_new_order()
    {
        var (page, _) = await RenderPageAsync(Permissions.LevelTypes.View, Permissions.LevelTypes.Update);

        await page.Find($"[data-testid=level-up-{_school.Id}]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Single(Org.Reorders));
        Assert.Equal([_school.Id, _group.Id, _unused.Id], Org.Reorders[0].OrderedIds);
    }

    [Fact]
    public async Task First_row_cannot_move_up_and_last_row_cannot_move_down()
    {
        var (page, _) = await RenderPageAsync(Permissions.LevelTypes.View, Permissions.LevelTypes.Update);

        Assert.True(page.Find($"[data-testid=level-up-{_group.Id}]").HasAttribute("disabled"));
        Assert.True(page.Find($"[data-testid=level-down-{_unused.Id}]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Delete_used_level_type_shows_level_type_in_use_message()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        Org.DeleteFailure = new ApiProblemException(409, "level_type_in_use", null, null);
        var (page, dialogs) = await RenderPageAsync(Permissions.LevelTypes.View, Permissions.LevelTypes.Delete);

        _ = page.Find($"[data-testid=level-delete-{_school.Id}]").ClickAsync(new()); // handler awaits the confirm dialog
        dialogs.WaitForElement("[data-testid=confirm-yes]");
        await dialogs.Find("[data-testid=confirm-yes]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Contains("This level type is used by existing units.", page.Find("[data-testid=level-error]").TextContent));
    }

    [Fact]
    public async Task Delete_confirmed_calls_the_api()
    {
        var (page, dialogs) = await RenderPageAsync(Permissions.LevelTypes.View, Permissions.LevelTypes.Delete);

        _ = page.Find($"[data-testid=level-delete-{_unused.Id}]").ClickAsync(new()); // handler awaits the confirm dialog
        dialogs.WaitForElement("[data-testid=confirm-yes]");
        await dialogs.Find("[data-testid=confirm-yes]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Equal(_unused.Id, Assert.Single(Org.Deleted)));
    }

    [Fact]
    public async Task Level_type_actions_hidden_without_permissions()
    {
        var (page, _) = await RenderPageAsync(Permissions.LevelTypes.View);

        Assert.Empty(page.FindAll("[data-testid=level-new]"));
        Assert.Empty(page.FindAll($"[data-testid=level-edit-{_group.Id}]"));
        Assert.Empty(page.FindAll($"[data-testid=level-delete-{_group.Id}]"));
        Assert.Empty(page.FindAll($"[data-testid=level-up-{_school.Id}]"));
    }

    [Fact]
    public async Task Out_of_scope_admin_sees_the_top_level_only_message()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        Org.DeleteFailure = new ApiProblemException(403, "OutOfScope", null, null);
        var (page, dialogs) = await RenderPageAsync(Permissions.LevelTypes.View, Permissions.LevelTypes.Delete);

        _ = page.Find($"[data-testid=level-delete-{_unused.Id}]").ClickAsync(new());
        dialogs.WaitForElement("[data-testid=confirm-yes]");
        await dialogs.Find("[data-testid=confirm-yes]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Contains("top level of the organization", page.Find("[data-testid=level-error]").TextContent));
    }

    [Fact]
    public async Task Host_classes_column_uses_words_for_screen_readers()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        var (page, _) = await RenderPageAsync(Permissions.LevelTypes.View);

        var row = page.Find($"[data-testid=level-row-{_unused.Id}]").TextContent;
        Assert.Contains("Yes", row);
        Assert.DoesNotContain("✓", row);
    }

    [Fact]
    public async Task Create_adds_a_level_type()
    {
        var (page, dialogs) = await RenderPageAsync(Permissions.LevelTypes.View, Permissions.LevelTypes.Create);

        _ = page.Find("[data-testid=level-new]").ClickAsync(new()); // handler awaits the dialog
        dialogs.WaitForElement("[data-testid=dialog-save]");
        dialogs.Find("input[data-testid=level-name-ar]").Change("الفرع");
        dialogs.Find("input[data-testid=level-name-en]").Change("Branch");
        await dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Contains(Org.Levels, l => l.NameEn == "Branch"));
    }

    [Theory]
    [InlineData("ar", "أنواع المستويات")]
    [InlineData("en", "Level types")]
    public async Task Page_is_localized(string culture, string title)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var (page, _) = await RenderPageAsync(Permissions.LevelTypes.View);
        Assert.Contains(title, page.Find("[data-testid=level-types-title]").TextContent);
    }
}
