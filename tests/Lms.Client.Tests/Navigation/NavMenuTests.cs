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
    public async Task Messages_entry_needs_messaging_view()
    {
        var without = await RenderNavAsync([Permissions.Roles.View]);
        Assert.Empty(without.FindAll("[data-testid=nav-messages]"));

        var with = await RenderNavAsync([Permissions.Messaging.View]);
        Assert.NotEmpty(with.FindAll("[data-testid=nav-messages]"));
    }

    [Fact]
    public async Task Unread_badge_counts_conversations_and_its_name_says_so()
    {
        MessagingApi.Me = Lms.Client.Services.Messaging.FakeMessagingApi.Huda;
        await MessagingApi.CreateConversationAsync(new Lms.Shared.Messaging.CreateConversationRequest(
            Lms.Shared.Messaging.ConversationKinds.Direct, [Lms.Client.Services.Messaging.FakeMessagingApi.Sara.Id], null, "hi"));
        MessagingApi.Me = Lms.Client.Services.Messaging.FakeMessagingApi.Sara;

        var cut = await RenderNavAsync([Permissions.Messaging.View]);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=nav-messages-badge]")));

        var badge = cut.Find("[data-testid=nav-messages-badge]");
        Assert.Contains("1", badge.TextContent);
        Assert.Contains("1", badge.GetAttribute("aria-label"));
    }

    [Fact]
    public async Task No_badge_when_nothing_is_unread()
    {
        var cut = await RenderNavAsync([Permissions.Messaging.View]);
        Assert.Empty(cut.FindAll("[data-testid=nav-messages-badge]"));
    }
}
