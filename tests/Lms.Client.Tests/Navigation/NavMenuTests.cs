using Bunit;
using Lms.Client.Layout;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Me;

namespace Lms.Client.Tests.Navigation;

public sealed class NavMenuTests : ClientTestContext
{
    private async Task<IRenderedComponent<NavMenu>> RenderNavAsync(string[] permissions, bool platformAdmin = false)
    {
        Api.Me = Api.Me with { IsPlatformAdmin = platformAdmin };
        Api.Permissions = new EffectivePermissionsDto(null, permissions);
        await Context.SetAsync(Guid.NewGuid());
        var cut = Render<NavMenu>();
        cut.WaitForState(() => cut.FindAll("[data-testid^=nav-]").Count > 0);
        return cut;
    }

    [Fact]
    public async Task NavMenu_hides_items_without_permission()
    {
        var cut = await RenderNavAsync([Permissions.Roles.View]);

        Assert.NotEmpty(cut.FindAll("[data-testid=nav-home]"));
        Assert.NotEmpty(cut.FindAll("[data-testid=nav-roles]"));
        Assert.Empty(cut.FindAll("[data-testid=nav-audit]"));
        Assert.Empty(cut.FindAll("[data-testid=nav-settings]"));
        Assert.Empty(cut.FindAll("[data-testid=nav-orgunits]"));
    }

    [Fact]
    public async Task NavMenu_shows_platform_items_only_to_platform_admin()
    {
        var admin = await RenderNavAsync([], platformAdmin: true);
        Assert.NotEmpty(admin.FindAll("[data-testid=nav-clients]"));
    }

    [Fact]
    public async Task NavMenu_hides_platform_items_from_regular_users()
    {
        var user = await RenderNavAsync([Permissions.Audit.View]);
        Assert.Empty(user.FindAll("[data-testid=nav-clients]"));
    }

    [Fact]
    public async Task Logout_clears_context_and_cached_permissions()
    {
        var cut = await RenderNavAsync([Permissions.Roles.View]);
        var state = Services.GetService(typeof(Lms.Client.Services.PermissionState)) as Lms.Client.Services.PermissionState;

        await Context.ClearAsync();
        cut.WaitForState(() => !cut.FindAll("[data-testid=nav-roles]").Any());

        Assert.False(state!.Has(Permissions.Roles.View));
    }

    [Fact]
    public async Task NavMenu_groups_items_under_labelled_sections_in_catalog_order()
    {
        var cut = await RenderNavAsync([Permissions.OrgUnits.View, Permissions.Staff.View, Permissions.Courses.View, Permissions.Roles.View]);

        var groups = cut.FindAll("[data-testid^=navsection-]");
        Assert.Equal(["navsection-structure", "navsection-people", "navsection-academic", "navsection-security"],
            groups.Select(g => g.GetAttribute("data-testid")!).ToArray());
        foreach (var g in groups)
        {
            Assert.Equal("group", g.GetAttribute("role"));
            var label = cut.Find($"#{g.GetAttribute("aria-labelledby")}");
            Assert.Contains("lms-nav-section", label.ClassName);
            Assert.False(string.IsNullOrWhiteSpace(label.TextContent));
        }
        Assert.NotNull(groups[0].QuerySelector("[data-testid=nav-orgunits]"));
        Assert.NotNull(groups[3].QuerySelector("[data-testid=nav-roles]"));
    }

    [Fact]
    public async Task NavMenu_keeps_home_outside_the_sections_and_hides_empty_sections()
    {
        var cut = await RenderNavAsync([Permissions.Roles.View]);

        Assert.Null(cut.Find("[data-testid=nav-home]").Closest("[data-testid^=navsection-]"));
        Assert.Equal(["navsection-security"], cut.FindAll("[data-testid^=navsection-]").Select(g => g.GetAttribute("data-testid")!).ToArray());
    }

    [Fact]
    public void Every_nav_item_has_a_section_except_home_and_uses_rounded_icons()
    {
        foreach (var item in Lms.Client.Navigation.NavCatalog.Items)
        {
            if (item.Key == "home") Assert.Null(item.Section);
            else Assert.False(string.IsNullOrEmpty(item.Section), item.Key);
        }
        Assert.All(Lms.Client.Navigation.NavCatalog.Items, i => Assert.Contains(i.Icon, RoundedIcons()));
    }

    private static HashSet<string> RoundedIcons() =>
        typeof(MudBlazor.Icons.Material.Rounded).GetFields().Select(f => (string)f.GetValue(null)!).ToHashSet();
}
