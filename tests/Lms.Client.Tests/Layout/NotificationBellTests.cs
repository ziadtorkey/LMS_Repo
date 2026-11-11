using System.Globalization;
using Bunit;
using Lms.Client.Layout;
using Lms.Client.Tests.Support;
using Lms.Shared.Notifications;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Layout;

public sealed class NotificationBellTests : ClientTestContext
{
    private IRenderedComponent<MudSnackbarProvider> _snackbars = default!;
    private IRenderedComponent<MudPopoverProvider> _popover = default!;

    private async Task<IRenderedComponent<NotificationBell>> RenderAsync()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        await GrantAsync();
        _popover = Render<MudPopoverProvider>();
        _snackbars = Render<MudSnackbarProvider>();
        return Render<NotificationBell>();
    }

    private static NotificationDto Live() => new(Guid.NewGuid(), NotificationEvents.AccessRoleAssigned,
        new Dictionary<string, string> { ["RoleNameEn"] = "Admin", ["RoleNameAr"] = "مسؤول", ["OrgUnitNameEn"] = "School", ["OrgUnitNameAr"] = "مدرسة" },
        "/roles", DateTime.UtcNow, false);

    [Fact]
    public async Task Bell_shows_unread_count_and_updates_on_realtime_message()
    {
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.Contains("2", cut.Find("[data-testid=notif-badge]").TextContent));

        NotificationConnection.Push(Live());

        cut.WaitForAssertion(() => Assert.Contains("3", cut.Find("[data-testid=notif-badge]").TextContent));
    }

    [Fact]
    public async Task Realtime_message_shows_a_snackbar()
    {
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=notif-badge]")));

        NotificationConnection.Push(Live());

        _snackbars.WaitForAssertion(() => Assert.Contains("You were given the role Admin at School.", _snackbars.Markup));
    }

    [Fact]
    public async Task Click_marks_read_and_navigates_to_deep_link()
    {
        var cut = await RenderAsync();
        var unread = NotificationsApi.Items.First(n => !n.IsRead && n.DeepLink is not null);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=notif-badge]")));
        await cut.Find(".mud-menu-activator").KeyDownAsync(Bunit.Key.Enter);

        await _popover.WaitForElement($"[data-testid=notif-item-{unread.Id}]").ClickAsync(new());

        Assert.Contains(NotificationsApi.Items, n => n.Id == unread.Id && n.IsRead);
        Assert.EndsWith("/assignments", Services.GetRequiredService<NavigationManager>().Uri);
    }
}
