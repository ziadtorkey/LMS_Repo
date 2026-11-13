using System.Globalization;
using Bunit;
using Lms.Client.Components.Content;
using Lms.Client.Services;
using Lms.Client.Resources;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Lms.Client.Tests.LiveSessions;

public sealed class ModuleKindLabelTests : ClientTestContext
{
    private static readonly string[] NewKinds = [ModuleKinds.LiveSession, ModuleKinds.Scorm, ModuleKinds.H5p];

    private string Label(string culture, string key)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var l = Services.GetRequiredService<IStringLocalizer<SharedResource>>();
        Assert.False(l[key].ResourceNotFound, $"{key} missing in {culture}");
        return l[key].Value;
    }

    [Fact]
    public void Module_kinds_have_labels_and_icons_in_both_languages()
    {
        foreach (var kind in ModuleKinds.All)
        {
            var ar = Label("ar", "ModuleKind_" + kind);
            var en = Label("en", "ModuleKind_" + kind);

            Assert.NotEqual(ar, en);
            Assert.NotEqual(ModuleKindIcons.Fallback, ModuleKindIcons.For(kind));
        }
        Assert.Equal(ModuleKinds.All.Count, ModuleKinds.All.Select(ModuleKindIcons.For).Distinct().Count());
        Assert.Equal(ModuleKindIcons.Fallback, ModuleKindIcons.For("SomethingNew"));
    }

    [Fact]
    public void Live_status_and_match_state_labels_exist_in_both_languages()
    {
        foreach (var culture in new[] { "ar", "en" })
        {
            foreach (var status in LiveSessionStatuses.All) Assert.NotEmpty(Label(culture, "LiveSession_Status_" + status));
            foreach (var state in ParticipantMatchStates.All) Assert.NotEmpty(Label(culture, "Live_Match_" + state));
            foreach (var scope in SeriesEditScopes.All) Assert.NotEmpty(Label(culture, "Live_Scope_" + scope));
        }
    }

    [Theory]
    [InlineData(ModuleKinds.LiveSession, "en")]
    [InlineData(ModuleKinds.Scorm, "en")]
    [InlineData(ModuleKinds.H5p, "en")]
    [InlineData(ModuleKinds.LiveSession, "ar")]
    public void Module_node_shows_the_kind_label_and_icon_of_the_new_kinds(string kind, string culture)
    {
        Culture.Current = new CultureInfo(culture);
        var node = new ModuleNodeDto(Guid.NewGuid(), kind, 1, "عنوان", "Title", ModuleVisibilities.Published, null, null,
            ModuleTargets.ClassCourse, null, null, null, CompletionRules.None, true, null);

        var cut = Render<ModuleNode>(p => p.Add(x => x.Node, node));

        var icon = cut.Find("[data-testid=module-kind]");
        Assert.Equal(Label(culture, "ModuleKind_" + kind), icon.GetAttribute("aria-label"));
        Assert.Contains("<title>" + Label(culture, "ModuleKind_" + kind) + "</title>", icon.InnerHtml);
    }

    [Theory]
    [InlineData(ModuleKinds.LiveSession)]
    [InlineData(ModuleKinds.Scorm)]
    [InlineData(ModuleKinds.H5p)]
    public void Module_body_shows_a_placeholder_for_the_new_kinds_until_their_viewers_land(string kind)
    {
        Culture.Current = new CultureInfo("en");
        var cut = Render<ModuleBody>(p => p.Add(x => x.Kind, kind));

        var placeholder = cut.Find("[data-testid=module-body-placeholder]");
        Assert.Equal(kind, placeholder.GetAttribute("data-kind"));
        Assert.Contains(Label("en", "ModuleKind_" + kind), placeholder.TextContent);
    }

    [Fact]
    public void Module_body_renders_nothing_for_kinds_that_have_their_own_viewer()
    {
        var cut = Render<ModuleBody>(p => p.Add(x => x.Kind, ModuleKinds.Page));

        Assert.Empty(cut.FindAll("[data-testid=module-body-placeholder]"));
    }

    [Fact]
    public async Task Add_module_menu_offers_only_the_kinds_created_by_upload_or_form()
    {
        await GrantAsync(Permissions.Modules.Manage);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        var popovers = Render<MudPopoverProvider>();
        var cut = Render<AddModuleMenu>(p => p.Add(x => x.ClassCourseId, Guid.NewGuid()).Add(x => x.LessonKey, Guid.NewGuid()));

        cut.Find("[data-testid=module-add-menu] button").Click();

        // Live sessions are scheduled from the live session page; packages are uploaded from the package page.
        foreach (var kind in NewKinds) Assert.Empty(popovers.FindAll($"[data-testid=module-add-{kind}]"));
        Assert.NotEmpty(popovers.FindAll($"[data-testid=module-add-{ModuleKinds.Page}]"));
    }
}
