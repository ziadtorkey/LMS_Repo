using System.Globalization;
using Bunit;
using Lms.Client.Pages.Notifications;
using Lms.Client.Tests.Support;
using Lms.Shared.Notifications;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Notifications;

public sealed class NotificationsPagesTests : ClientTestContext
{
    private IRenderedComponent<TPage> Page<TPage>() where TPage : Microsoft.AspNetCore.Components.IComponent
    {
        Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
        return Render<TPage>();
    }

    [Fact]
    public async Task List_shows_unread_marker_and_unread_filter_hides_read_ones()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync();
        var cut = Page<NotificationsPage>();
        var read = NotificationsApi.Items.First(n => n.IsRead);

        cut.WaitForElement($"[data-testid=notif-row-{read.Id}]");
        Assert.Equal(2, cut.FindAll("[data-testid^=notif-unread-]").Count);

        await cut.Find("[data-testid=notif-only-unread]").ChangeAsync(new() { Value = true });

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll($"[data-testid=notif-row-{read.Id}]")));
    }

    [Fact]
    public async Task Mark_all_read_clears_unread_markers()
    {
        await GrantAsync();
        var cut = Page<NotificationsPage>();
        cut.WaitForElement("[data-testid^=notif-unread-]");

        await cut.Find("[data-testid=notif-mark-all]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid^=notif-unread-]")));
    }

    [Fact]
    public async Task Offline_mark_all_shows_a_snackbar_instead_of_crashing()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync();
        Render<MudPopoverProvider>();
        var snackbars = Render<MudSnackbarProvider>();
        var cut = Render<NotificationsPage>();
        cut.WaitForElement("[data-testid^=notif-unread-]");
        NotificationsApi.NextFailure = new HttpRequestException("offline");

        await cut.Find("[data-testid=notif-mark-all]").ClickAsync(new());

        snackbars.WaitForAssertion(() => Assert.Contains("Something went wrong", snackbars.Markup));
    }

    [Fact]
    public async Task In_app_preference_switch_is_locked_on()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync();
        var cut = Page<NotificationPreferencesPage>();

        var inApp = cut.WaitForElement($"[data-testid=\"pref-{NotificationEvents.AccessRoleAssigned}-InApp\"]");
        var email = cut.Find($"[data-testid=\"pref-{NotificationEvents.AccessRoleAssigned}-Email\"]");

        Assert.True(inApp.HasAttribute("checked")); // reads as ON, not as a greyed-out OFF
        Assert.False(inApp.HasAttribute("disabled"));
        Assert.False(email.HasAttribute("readonly"));
        await inApp.ChangeAsync(new() { Value = false });
        await cut.Find("[data-testid=prefs-save]").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.NotNull(NotificationsApi.LastSaved));
        Assert.Contains(NotificationsApi.LastSaved!.Preferences, p => p.Channel == "InApp" && p.Enabled);
        Assert.All(NotificationsApi.LastSaved!.Preferences.Where(p => p.Channel == "InApp"), p => Assert.True(p.Enabled));
    }

    [Fact]
    public async Task Saving_preferences_sends_the_changed_switches()
    {
        await GrantAsync();
        var cut = Page<NotificationPreferencesPage>();
        var push = cut.WaitForElement($"[data-testid=\"pref-{NotificationEvents.AccessRoleAssigned}-Push\"]");
        await push.ChangeAsync(new() { Value = true });

        await cut.Find("[data-testid=prefs-save]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.NotNull(NotificationsApi.LastSaved));
        Assert.Contains(NotificationsApi.LastSaved!.Preferences,
            p => p.EventKey == NotificationEvents.AccessRoleAssigned && p.Channel == "Push" && p.Enabled);
    }

    [Fact]
    public async Task Pages_render_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync();
        Render<MudPopoverProvider>();
        var list = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<NotificationsPage>());
        list.WaitForElement("[data-testid^=notif-row-]");
        Assert.Contains("الإشعارات", list.Markup);
        Assert.Contains("تم منحك دور المعلم في الصف الأول.", list.Markup);

        var prefs = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<NotificationPreferencesPage>());
        prefs.WaitForElement("[data-testid=prefs-save]");
        Assert.Contains("تفضيلات الإشعارات", prefs.Markup);
    }
}
