using System.Globalization;
using Bunit;
using Lms.Client.Pages.Organization;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Organization;

public sealed class OrgTreePageTests : ClientTestContext
{
    private readonly Guid _rootId = Guid.NewGuid(), _schoolId = Guid.NewGuid(), _blockedId = Guid.NewGuid(), _inactiveId = Guid.NewGuid();
    private readonly Guid _groupLevel = Guid.NewGuid(), _schoolLevel = Guid.NewGuid();

    private OrgUnitNodeDto Unit(Guid id, Guid? parent, Guid level, string en, string status = "Active", bool children = false, params string[] blocked) =>
        new(id, parent, level, "ع-" + en, en, en.ToUpperInvariant(), parent is null ? 0 : 1, status, false, null, false, children, blocked);

    private async Task<(IRenderedComponent<OrgTreePage> Page, IRenderedComponent<MudDialogProvider> Dialogs)> RenderPageAsync(params string[] permissions)
    {
        Culture.Current = new CultureInfo("en");
        CultureInfo.CurrentUICulture = Culture.Current;
        Org.Levels.AddRange([new(_groupLevel, "المجموعة", "Group", 1, false, 1), new(_schoolLevel, "المدرسة", "School", 2, false, 1)]);
        Org.Units.AddRange(
        [
            Unit(_rootId, null, _groupLevel, "Root", children: true),
            Unit(_schoolId, _rootId, _schoolLevel, "North School"),
            Unit(_blockedId, _rootId, _schoolLevel, "Blocked School", blocked: Permissions.OrgUnits.Create),
            Unit(_inactiveId, _rootId, _schoolLevel, "Closed School", status: "Inactive"),
        ]);
        await GrantAsync(permissions);
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var page = Render<OrgTreePage>();
        page.WaitForElement($"[data-testid=org-node-{_schoolId}]");
        return (page, dialogs);
    }

    private static readonly string[] All =
    [
        Permissions.OrgUnits.View, Permissions.OrgUnits.Create, Permissions.OrgUnits.Update, Permissions.OrgUnits.Delete,
    ];

    [Fact]
    public async Task Add_child_hidden_when_policy_blocks_it()
    {
        var (page, _) = await RenderPageAsync(All);

        Assert.NotEmpty(page.FindAll($"[data-testid=org-add-{_schoolId}]"));
        Assert.Empty(page.FindAll($"[data-testid=org-add-{_blockedId}]"));
    }

    [Fact]
    public async Task Actions_are_hidden_without_permissions()
    {
        var (page, _) = await RenderPageAsync(Permissions.OrgUnits.View);

        Assert.Empty(page.FindAll($"[data-testid=org-add-{_schoolId}]"));
        Assert.Empty(page.FindAll($"[data-testid=org-edit-{_schoolId}]"));
        Assert.Empty(page.FindAll($"[data-testid=org-toggle-{_schoolId}]"));
        Assert.Empty(page.FindAll($"[data-testid=org-delete-{_schoolId}]"));
        Assert.NotEmpty(page.FindAll($"[data-testid=org-settings-{_schoolId}]"));
    }

    [Fact]
    public async Task Root_cannot_be_deactivated_or_deleted()
    {
        var (page, _) = await RenderPageAsync(All);

        Assert.Empty(page.FindAll($"[data-testid=org-toggle-{_rootId}]"));
        Assert.Empty(page.FindAll($"[data-testid=org-delete-{_rootId}]"));
    }

    [Fact]
    public async Task Create_dialog_posts_parent_and_level_type()
    {
        var (page, dialogs) = await RenderPageAsync(All);

        _ = page.Find($"[data-testid=org-add-{_rootId}]").ClickAsync(new()); // the handler awaits the dialog
        dialogs.WaitForElement("[data-testid=dialog-save]");
        dialogs.Find("input[data-testid=unit-name-ar]").Change("وحدة جديدة");
        dialogs.Find("input[data-testid=unit-name-en]").Change("New Unit");
        dialogs.Find("input[data-testid=unit-code]").Change("NEW-1");
        await dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Single(Org.UnitsCreated));
        var sent = Org.UnitsCreated[0];
        Assert.Equal(_rootId, sent.ParentId);
        Assert.Equal(_schoolLevel, sent.LevelTypeId); // first level type allowed below the root
        Assert.Equal("NEW-1", sent.Code);
        Assert.Equal("New Unit", sent.NameEn);
    }

    [Fact]
    public async Task Delete_with_children_shows_error_message()
    {
        Org.UnitDeleteFailure = new ApiProblemException(409, "unit_has_children", null, null);
        var (page, dialogs) = await RenderPageAsync(All);

        _ = page.Find($"[data-testid=org-delete-{_schoolId}]").ClickAsync(new());
        dialogs.WaitForElement("[data-testid=confirm-yes]");
        await dialogs.Find("[data-testid=confirm-yes]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Contains("Remove or move the child units first.", page.Find("[data-testid=org-error]").TextContent));
    }

    [Fact]
    public async Task Inactive_node_shows_chip_and_activate_action()
    {
        var (page, _) = await RenderPageAsync(All);

        Assert.Contains("Inactive", page.Find($"[data-testid=org-node-{_inactiveId}]").TextContent);
        Assert.Contains("Activate", page.Find($"[data-testid=org-toggle-{_inactiveId}]").GetAttribute("aria-label"));

        await page.Find($"[data-testid=org-toggle-{_inactiveId}]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Equal((_inactiveId, "Active"), Assert.Single(Org.StatusChanges)));
    }

    [Fact]
    public async Task Search_asks_the_server_and_shows_a_message_when_nothing_matches()
    {
        var (page, _) = await RenderPageAsync(All);

        page.Find("input[data-testid=org-search]").Input("zzz");

        page.WaitForAssertion(() => Assert.Contains("zzz", Org.UnitSearches), TimeSpan.FromSeconds(3));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=org-no-matches]")), TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData("ar", "الهيكل التنظيمي")]
    [InlineData("en", "Organization tree")]
    public async Task Page_is_localized(string culture, string title)
    {
        var (page, _) = await RenderPageAsync(All);
        Culture.Current = new CultureInfo(culture);
        CultureInfo.CurrentUICulture = Culture.Current;
        var fresh = Render<OrgTreePage>();
        fresh.WaitForElement("[data-testid=org-title]");
        Assert.Contains(title, fresh.Find("[data-testid=org-title]").TextContent);
        Assert.NotNull(page);
    }
}
