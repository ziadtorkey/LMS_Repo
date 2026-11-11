using System.Globalization;
using Bunit;
using Lms.Client.Layout;
using Lms.Client.Pages.Organization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Organization;

public sealed class UnitSettingsTests : ClientTestContext
{
    private readonly Guid _level = Guid.NewGuid(), _otherLevel = Guid.NewGuid();
    private readonly Guid _rootId = Guid.NewGuid(), _kgId = Guid.NewGuid(), _primaryId = Guid.NewGuid(), _preparatoryId = Guid.NewGuid(),
        _otherTypeId = Guid.NewGuid(), _childOfKgId = Guid.NewGuid(), _plainId = Guid.NewGuid();

    private OrgUnitNodeDto U(Guid id, Guid? parent, Guid level, string en, bool transfer = false, Guid? next = null, bool course = false) =>
        new(id, parent, level, "ع-" + en, en, en.ToUpperInvariant(), parent is null ? 0 : 1, "Active", transfer, next, course, false, []);

    private List<OrgUnitNodeDto> Units() =>
    [
        U(_rootId, null, _otherLevel, "Root"),
        U(_kgId, _rootId, _level, "KG", transfer: true, next: _primaryId),
        U(_primaryId, _rootId, _level, "Primary", transfer: true, next: _preparatoryId),
        U(_preparatoryId, _rootId, _level, "Prep", transfer: true),
        U(_otherTypeId, _rootId, _otherLevel, "Other type", transfer: true),
        U(_childOfKgId, _kgId, _level, "KG child", transfer: true),
        U(_plainId, _rootId, _level, "Plain"),
    ];

    // --- Move dialog ---

    [Fact]
    public void Move_dialog_excludes_own_branch_from_targets()
    {
        var targets = MoveUnitDialog.Candidates(Units().First(u => u.Id == _kgId), Units()).Select(u => u.Id).ToList();

        Assert.DoesNotContain(_kgId, targets);        // itself
        Assert.DoesNotContain(_childOfKgId, targets); // its own descendant
        Assert.DoesNotContain(_rootId, targets);      // current parent: moving there is a no-op
        Assert.Contains(_primaryId, targets);
    }

    [Fact]
    public async Task Move_posts_the_chosen_parent()
    {
        Culture.Current = new CultureInfo("en");
        CultureInfo.CurrentUICulture = Culture.Current;
        var units = Units();
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var service = Services.GetService(typeof(IDialogService)) as IDialogService;

        _ = service!.ShowAsync<MoveUnitDialog>("Move", new DialogParameters<MoveUnitDialog>
        {
            { x => x.Unit, units.First(u => u.Id == _plainId) }, { x => x.AllUnits, units },
        });
        dialogs.WaitForElement("[data-testid=dialog-save]");
        Assert.True(dialogs.Find("[data-testid=dialog-save]").HasAttribute("disabled")); // nothing preselected

        var select = dialogs.FindComponent<MudSelect<Guid?>>();
        await dialogs.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync((Guid?)_primaryId));
        dialogs.WaitForAssertion(() => Assert.False(dialogs.Find("[data-testid=dialog-save]").HasAttribute("disabled")));
        await dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        dialogs.WaitForAssertion(() => Assert.Single(Org.Moves));
        Assert.Equal(_plainId, Org.Moves[0].Id);
        Assert.Equal(_primaryId, Org.Moves[0].Request.NewParentId);
    }

    [Fact]
    public async Task Move_hidden_without_OrgUnits_Move()
    {
        Org.Units.AddRange(Units());
        await GrantAsync(Permissions.OrgUnits.View, Permissions.OrgUnits.Update);
        Render<MudPopoverProvider>(); Render<MudDialogProvider>();
        var page = Render<OrgTreePage>();
        page.WaitForElement($"[data-testid=org-node-{_rootId}]");
        page.Find($"[data-testid=org-node-{_rootId}]");

        Assert.Empty(page.FindAll($"[data-testid=org-move-{_kgId}]"));
    }

    [Fact]
    public async Task Move_shown_with_OrgUnits_Move_but_never_for_the_root()
    {
        Org.Units.AddRange(Units());
        await GrantAsync(Permissions.OrgUnits.View, Permissions.OrgUnits.Move);
        Render<MudPopoverProvider>(); Render<MudDialogProvider>();
        var page = Render<OrgTreePage>();
        page.WaitForElement($"[data-testid=org-node-{_rootId}]");

        Assert.Empty(page.FindAll($"[data-testid=org-move-{_rootId}]"));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll($"[data-testid=org-move-{_kgId}]")));
    }

    [Fact]
    public async Task Stale_context_when_opening_move_goes_to_the_chooser()
    {
        Org.Units.AddRange(Units());
        await GrantAsync(Permissions.OrgUnits.View, Permissions.OrgUnits.Move);
        Render<MudPopoverProvider>(); Render<MudDialogProvider>();
        var page = Render<OrgTreePage>();
        page.WaitForElement($"[data-testid=org-move-{_kgId}]");

        Org.ListFailure = new Lms.Client.Services.Api.ApiContextException("invalid_context");
        await page.Find($"[data-testid=org-move-{_kgId}]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.EndsWith("/choose-context", Services.GetRequiredService<NavigationManager>().Uri));
        Assert.Empty(page.FindAll("[data-testid=org-error]"));
    }

    [Fact]
    public async Task Network_error_when_opening_move_shows_the_error_message()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        Org.Units.AddRange(Units());
        await GrantAsync(Permissions.OrgUnits.View, Permissions.OrgUnits.Move);
        Render<MudPopoverProvider>(); Render<MudDialogProvider>();
        var page = Render<OrgTreePage>();
        page.WaitForElement($"[data-testid=org-move-{_kgId}]");

        Org.ListFailure = new HttpRequestException("offline");
        await page.Find($"[data-testid=org-move-{_kgId}]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=org-error]")), TimeSpan.FromSeconds(3)); // text depends on the ambient culture
    }

    [Fact]
    public async Task Settings_page_tabs_render_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        CultureInfo.CurrentUICulture = Culture.Current;
        Org.Units.AddRange(Units());
        await GrantAsync(Permissions.OrgUnits.View);

        var layout = Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b =>
        {
            b.OpenComponent<UnitSettingsPage>(0);
            b.AddComponentParameter(1, "Id", _kgId);
            b.CloseComponent();
        })));

        layout.WaitForElement("[data-testid=unit-tabs]");
        Assert.Equal("rtl", layout.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains("عام", layout.Find("[data-testid=unit-tabs]").TextContent);
        Assert.Contains("ع-KG", layout.Find("[data-testid=unit-title]").TextContent);
    }

    // --- Academic tab ---

    [Fact]
    public void Next_level_picker_lists_same_level_type_transfer_units_only()
    {
        var kg = Units().First(u => u.Id == _kgId);
        var ids = AcademicFlagsTab.NextLevelCandidates(kg, Units()).Select(u => u.Id).ToList();

        Assert.Equal(new HashSet<Guid> { _childOfKgId, _preparatoryId, _primaryId }, ids.ToHashSet());
        Assert.DoesNotContain(_kgId, ids);        // not itself
        Assert.DoesNotContain(_otherTypeId, ids); // different level type
        Assert.DoesNotContain(_plainId, ids);     // not a transfer unit
    }

    private async Task<IRenderedComponent<AcademicFlagsTab>> RenderTabAsync(Guid unitId, bool canEdit = true)
    {
        Culture.Current = new CultureInfo("en");
        CultureInfo.CurrentUICulture = Culture.Current;
        Render<MudPopoverProvider>();
        var units = Units();
        var cut = Render<AcademicFlagsTab>(p => p
            .Add(t => t.Unit, units.First(u => u.Id == unitId)).Add(t => t.AllUnits, units).Add(t => t.CanEdit, canEdit));
        await Task.CompletedTask;
        return cut;
    }

    [Fact]
    public async Task Turning_off_transfer_clears_next_level()
    {
        var cut = await RenderTabAsync(_kgId);

        cut.Find("input[data-testid=flag-transfer]").Change(false);
        await cut.Find("[data-testid=academic-save]").ClickAsync(new());

        var (id, request) = Assert.Single(Org.FlagChanges);
        Assert.Equal(_kgId, id);
        Assert.False(request.IsLevelTransfer);
        Assert.Null(request.NextTransferUnitId);
    }

    [Fact]
    public async Task Transfer_path_shows_chain()
    {
        var cut = await RenderTabAsync(_kgId);

        Assert.Equal("KG → Primary → Prep", cut.Find("[data-testid=transfer-path]").TextContent.Trim());
    }

    [Fact]
    public async Task Course_management_flag_is_saved()
    {
        var cut = await RenderTabAsync(_plainId);

        cut.Find("input[data-testid=flag-course]").Change(true);
        await cut.Find("[data-testid=academic-save]").ClickAsync(new());

        Assert.True(Assert.Single(Org.FlagChanges).Request.IsCourseManagementLevel);
    }

    [Fact]
    public async Task Without_update_permission_the_tab_is_read_only()
    {
        var cut = await RenderTabAsync(_kgId, canEdit: false);

        Assert.Empty(cut.FindAll("[data-testid=academic-save]"));
    }

    // --- Page ---

    [Theory]
    [InlineData("ar", "عام")]
    [InlineData("en", "General")]
    public async Task Settings_page_tabs_are_localized(string culture, string general)
    {
        Culture.Current = new CultureInfo(culture);
        CultureInfo.CurrentUICulture = Culture.Current;
        Org.Units.AddRange(Units());
        await GrantAsync(Permissions.OrgUnits.View);
        Render<MudPopoverProvider>(); Render<MudDialogProvider>();

        var page = Render<UnitSettingsPage>(p => p.Add(x => x.Id, _kgId));

        page.WaitForAssertion(() => Assert.Contains(general, page.Find("[data-testid=unit-tabs]").TextContent));
    }
}
