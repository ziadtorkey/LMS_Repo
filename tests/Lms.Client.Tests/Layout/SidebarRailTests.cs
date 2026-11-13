using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using Lms.Client.Layout;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Me;
using MudBlazor;

namespace Lms.Client.Tests.Layout;

// The desktop sidebar collapses to a 72 px icon rail (issue 561). Below 960 px the drawer stays the temporary drawer.
public sealed class SidebarRailTests : ClientTestContext
{
    private IRenderedComponent<MainLayout> RenderLayout(string culture = "en")
    {
        Culture.Current = new CultureInfo(culture);
        CultureInfo.CurrentUICulture = Culture.Current;
        return Render<MainLayout>(p => p.Add(l => l.Body, b => b.AddMarkupContent(0, "<p>body</p>")));
    }

    [Fact]
    public async Task Rail_toggle_is_labelled_exposes_aria_expanded_and_remembers_the_choice()
    {
        var set = JSInterop.SetupVoid("lmsNav.setRailCollapsed", _ => true);
        set.SetVoidResult();
        var cut = RenderLayout();

        var toggle = cut.Find("[data-testid=rail-toggle]");
        Assert.Equal("Collapse sidebar", toggle.GetAttribute("aria-label"));
        Assert.Equal("true", toggle.GetAttribute("aria-expanded"));
        Assert.Equal("lms-sidebar", toggle.GetAttribute("aria-controls"));
        Assert.NotNull(cut.Find("#lms-sidebar"));
        Assert.DoesNotContain("lms-rail", cut.Find("[data-testid=app-root]").ClassList);

        await toggle.ClickAsync(new());

        cut.WaitForAssertion(() =>
        {
            var collapsed = cut.Find("[data-testid=rail-toggle]");
            Assert.Equal("Expand sidebar", collapsed.GetAttribute("aria-label"));
            Assert.Equal("false", collapsed.GetAttribute("aria-expanded"));
            Assert.Contains("lms-rail", cut.Find("[data-testid=app-root]").ClassList);
        });
        Assert.Equal(true, set.Invocations.Single().Arguments[0]);
    }

    [Fact]
    public void Layout_starts_as_a_rail_when_the_user_collapsed_it_before()
    {
        JSInterop.Setup<bool>("lmsNav.isRailCollapsed").SetResult(true);
        var cut = RenderLayout("ar");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("lms-rail", cut.Find("[data-testid=app-root]").ClassList);
            Assert.Equal("توسيع القائمة الجانبية", cut.Find("[data-testid=rail-toggle]").GetAttribute("aria-label"));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Nav_links_get_tooltips_only_as_a_rail_and_keep_their_text_as_the_name(bool collapsed)
    {
        Culture.Current = new CultureInfo("en");
        CultureInfo.CurrentUICulture = Culture.Current;
        Api.Permissions = new EffectivePermissionsDto(null, [Permissions.Roles.View]);
        await Context.SetAsync(Guid.NewGuid());
        var cut = Render<NavMenu>(p => p.Add(n => n.Collapsed, collapsed));
        cut.WaitForState(() => cut.FindAll("[data-testid=nav-roles]").Count > 0);

        var tooltip = cut.FindComponents<MudTooltip>().Single(t => t.FindAll("[data-testid=nav-roles]").Count > 0);
        Assert.Equal("Roles", tooltip.Instance.Text);
        Assert.Equal(!collapsed, tooltip.Instance.Disabled);
        Assert.Contains("Roles", cut.Find("[data-testid=nav-roles]").TextContent);
    }

    [Fact]
    public void Rail_width_applies_only_from_960_px_so_phones_keep_the_drawer()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lms.Client", "wwwroot", "css", "lms-stitch.css"));

        // MudBlazor's theme variable is --mud-drawer-width-mini-left; RTL anchors the drawer right, so both sides shrink.
        Assert.Matches(new Regex(@"@media \(min-width: 960px\) \{[^@]*?\.lms-app\.lms-rail \{ --mud-drawer-width-left: var\(--mud-drawer-width-mini-left\); --mud-drawer-width-right: var\(--mud-drawer-width-mini-left\); \}"), css);
    }

    [Fact]
    public void Rail_preference_is_read_and_written_behind_try_catch()
    {
        var js = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lms.Client", "wwwroot", "js", "nav.js"));

        Assert.Equal(2, Regex.Matches(js, @"try \{ [^}]*localStorage\.(getItem|setItem)\('lms\.nav\.rail'").Count);
        Assert.Contains("js/nav.js", File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lms.Client", "wwwroot", "index.html")));
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "LMS.slnx"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }
}
