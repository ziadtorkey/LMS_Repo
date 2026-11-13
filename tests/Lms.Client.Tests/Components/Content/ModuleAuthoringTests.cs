using Bunit;
using Lms.Client.Components.Content;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Components.Content;

public sealed class ModuleAuthoringTests : ClientTestContext
{
    private static readonly Guid Cc = Guid.NewGuid(), Lesson = Guid.NewGuid(), OtherLesson = Guid.NewGuid();

    private readonly FakeModulesApi _modules = new();
    private readonly FakeAssetsApi _assets = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;
    private IRenderedComponent<MudPopoverProvider> _popovers = default!;

    public ModuleAuthoringTests()
    {
        Services.AddSingleton<IModulesApi>(_modules);
        Services.AddSingleton<IAssetsApi>(_assets);
    }

    private async Task StartAsync(params string[] permissions)
    {
        await GrantAsync(permissions);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        _popovers = Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
    }

    private static CreateModuleRequest Page(string title, string visibility = ModuleVisibilities.Published,
        DateTime? from = null, DateTime? to = null, string target = ModuleTargets.ClassCourse, Guid? group = null) =>
        new(ModuleKinds.Page, title, title, visibility, from, to, target, group, CompletionRules.None, null,
            new ModuleContentInput(BodyHtmlEn: "<p>x</p>"));

    private IRenderedComponent<ModuleOrderControls> Controls(ModuleNodeDto module, bool readOnly = false) =>
        Render<ModuleOrderControls>(p => p
            .Add(x => x.ClassCourseId, Cc).Add(x => x.LessonKey, Lesson).Add(x => x.Module, module)
            .Add(x => x.Siblings, _modules.NodesFor(Cc, Lesson))
            .Add(x => x.OtherLessons, [new ModuleLessonOption(OtherLesson, "Lesson 2")])
            .Add(x => x.ReadOnly, readOnly));

    private void OpenDialog(string kind, ModuleDetailDto? existing = null) =>
        _ = Services.GetRequiredService<IDialogService>().ShowAsync<ModuleDialog>("t", new DialogParameters<ModuleDialog>
        {
            { x => x.ClassCourseId, Cc }, { x => x.LessonKey, Lesson }, { x => x.Kind, kind }, { x => x.Existing, existing },
        });

    [Fact]
    public async Task Teacher_adds_page_module_under_own_lesson()
    {
        await StartAsync(Permissions.Modules.Manage);
        OpenDialog(ModuleKinds.Page);
        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=module-save]")));

        _dialogs.Find("[data-testid=module-title-ar]").Change("مقدمة");
        _dialogs.Find("[data-testid=module-title-en]").Change("Intro");
        await _dialogs.InvokeAsync(() => _dialogs.FindComponents<Lms.Client.Components.Editors.RichTextEditor>()
            .First(e => e.Instance.TestId == "module-body-en").Instance.ValueChanged.InvokeAsync("<p>Hello</p>"));
        _dialogs.Find("[data-testid=module-save]").Click();

        _dialogs.WaitForAssertion(() => Assert.Single(_modules.NodesFor(Cc, Lesson)));
        var node = _modules.NodesFor(Cc, Lesson).Single();
        Assert.Equal(("Intro", ModuleKinds.Page, ModuleVisibilities.Published), (node.TitleEn, node.Kind, node.Visibility));
    }

    [Fact]
    public async Task Add_menu_lists_the_content_kinds()
    {
        await StartAsync(Permissions.Modules.Manage);
        var cut = Render<AddModuleMenu>(p => p.Add(x => x.ClassCourseId, Cc).Add(x => x.LessonKey, Lesson));

        cut.Find("[data-testid=module-add-menu] button").Click();

        foreach (var kind in ModuleKinds.Content)
            Assert.NotNull(_popovers.Find($"[data-testid=module-add-{kind}]"));
        Assert.Empty(_popovers.FindAll("[data-testid=module-add-Quiz]"));
    }

    [Fact]
    public async Task Scheduled_requires_from_before_to()
    {
        await StartAsync(Permissions.Modules.Manage);
        var now = DateTime.UtcNow;
        var seeded = _modules.Seed(Cc, Lesson, Page("Plan", ModuleVisibilities.Scheduled, now.AddDays(2), now.AddDays(1)));
        var detail = await _modules.GetAsync(seeded.Node.Id);
        var dialogs = Services.GetRequiredService<IDialogService>();

        _ = dialogs.ShowAsync<ModuleDialog>("t", new DialogParameters<ModuleDialog>
        {
            { x => x.ClassCourseId, Cc }, { x => x.LessonKey, Lesson }, { x => x.Kind, ModuleKinds.Page }, { x => x.Existing, detail },
        });
        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=module-save]")));
        _modules.Calls.Clear();

        _dialogs.Find("[data-testid=module-save]").Click();

        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=module-visibility-error]")));
        Assert.DoesNotContain(_modules.Calls, c => c.StartsWith("update"));
    }

    [Fact]
    public async Task Group_target_lists_only_groups_of_this_class_course()
    {
        await StartAsync(Permissions.Modules.Manage);
        var mine = new ModuleGroupOption(Guid.NewGuid(), "مجموعة أ", "Group A");
        var model = new ModuleFormModel { Target = ModuleTargets.Group };

        var cut = Render<ModuleMetadataForm>(p => p.Add(x => x.Model, model).Add(x => x.Groups, [mine]));

        var items = cut.FindComponents<MudSelectItem<Guid?>>();
        Assert.Equal([(Guid?)mine.Id], items.Select(i => i.Instance.Value));
    }

    [Fact]
    public async Task Reorder_by_keyboard_buttons()
    {
        await StartAsync(Permissions.Modules.Manage);
        _modules.Seed(Cc, Lesson, Page("A"));
        var b = _modules.Seed(Cc, Lesson, Page("B"));
        _modules.Seed(Cc, Lesson, Page("C"));
        var changed = 0;
        var cut = Render<ModuleOrderControls>(p => p
            .Add(x => x.ClassCourseId, Cc).Add(x => x.LessonKey, Lesson).Add(x => x.Module, b.Node)
            .Add(x => x.Siblings, _modules.NodesFor(Cc, Lesson))
            .Add(x => x.OnChanged, EventCallback.Factory.Create(this, () => changed++)));

        cut.Find($"[data-testid=module-up-{b.Node.Id}]").Click();

        cut.WaitForAssertion(() => Assert.Equal(1, changed));
        Assert.Equal(["B", "A", "C"], _modules.NodesFor(Cc, Lesson).Select(n => n.TitleEn));
    }

    [Fact]
    public async Task First_module_cannot_move_up_and_last_cannot_move_down()
    {
        await StartAsync(Permissions.Modules.Manage);
        var a = _modules.Seed(Cc, Lesson, Page("A"));
        var b = _modules.Seed(Cc, Lesson, Page("B"));

        Assert.True(Controls(a.Node).Find($"[data-testid=module-up-{a.Node.Id}]").HasAttribute("disabled"));
        Assert.True(Controls(b.Node).Find($"[data-testid=module-down-{b.Node.Id}]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Hide_then_show_toggles_visibility()
    {
        await StartAsync(Permissions.Modules.Manage);
        var a = _modules.Seed(Cc, Lesson, Page("A"));
        var cut = Controls(a.Node);

        cut.Find($"[data-testid=module-toggle-{a.Node.Id}]").Click();

        cut.WaitForAssertion(() => Assert.Equal(ModuleVisibilities.Hidden, _modules.NodesFor(Cc, Lesson).Single().Visibility));
    }

    [Fact]
    public async Task A_scheduled_module_has_no_hide_toggle_so_its_schedule_is_never_lost()
    {
        await StartAsync(Permissions.Modules.Manage);
        var from = DateTime.UtcNow.AddDays(1);
        var to = from.AddDays(7);
        var scheduled = _modules.Seed(Cc, Lesson, Page("Later", ModuleVisibilities.Scheduled, from, to));

        var cut = Controls(scheduled.Node);

        Assert.Empty(cut.FindAll($"[data-testid=module-toggle-{scheduled.Node.Id}]"));
        var node = _modules.NodesFor(Cc, Lesson).Single();
        Assert.Equal((ModuleVisibilities.Scheduled, from, to), (node.Visibility, node.VisibleFromUtc, node.VisibleToUtc));
    }

    [Fact]
    public async Task Delete_asks_for_confirmation()
    {
        await StartAsync(Permissions.Modules.Manage);
        var a = _modules.Seed(Cc, Lesson, Page("A"));
        var cut = Controls(a.Node);

        cut.Find($"[data-testid=module-delete-{a.Node.Id}]").Click();
        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=confirm-yes]")));
        Assert.Single(_modules.NodesFor(Cc, Lesson));
        _dialogs.Find("[data-testid=confirm-yes]").Click();

        cut.WaitForAssertion(() => Assert.Empty(_modules.NodesFor(Cc, Lesson)));
    }

    [Fact]
    public async Task Locked_blueprint_nodes_have_no_edit_controls()
    {
        await StartAsync(Permissions.Modules.Manage);
        var a = _modules.Seed(Cc, Lesson, Page("A"));

        var controls = Controls(a.Node, readOnly: true);
        var menu = Render<AddModuleMenu>(p => p.Add(x => x.ClassCourseId, Cc).Add(x => x.LessonKey, Lesson).Add(x => x.ReadOnly, true));

        Assert.Empty(controls.FindAll("button"));
        Assert.Empty(menu.FindAll("[data-testid=module-add-menu]"));
    }

    [Fact]
    public async Task Student_sees_no_authoring_controls()
    {
        await StartAsync(Permissions.Modules.View);
        var a = _modules.Seed(Cc, Lesson, Page("A"));

        var controls = Controls(a.Node);
        var menu = Render<AddModuleMenu>(p => p.Add(x => x.ClassCourseId, Cc).Add(x => x.LessonKey, Lesson));

        Assert.Empty(controls.FindAll("button"));
        Assert.Empty(menu.FindAll("[data-testid=module-add-menu]"));
    }
}
