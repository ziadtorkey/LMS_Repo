using Lms.Client.Services;
using Lms.Client.Tests.Support;
using Lms.Shared.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Services;

public sealed class NotificationFeedTests : ClientTestContext
{
    private INotificationFeed Feed => Services.GetRequiredService<INotificationFeed>();

    private static NotificationDto Live(bool read = false) => new(Guid.NewGuid(), NotificationEvents.AccessRoleAssigned,
        new Dictionary<string, string> { ["RoleNameEn"] = "Teacher", ["RoleNameAr"] = "المعلم", ["OrgUnitNameEn"] = "G1", ["OrgUnitNameAr"] = "ص١" },
        null, DateTime.UtcNow, read);

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }

    [Fact]
    public void Hub_url_carries_the_context()
    {
        var id = Guid.NewGuid();
        Assert.Equal($"https://lms.test/hubs/notifications?context={id}", Lms.Client.Services.NotificationConnection.HubUrl("https://lms.test/", id));
    }

    [Fact]
    public async Task Connects_with_the_active_context()
    {
        await GrantAsync();
        await Feed.StartAsync();

        Assert.True(base.NotificationConnection.IsStarted);
        Assert.Equal(Context.ActiveAssignmentId, base.NotificationConnection.StartedWith);
        Assert.Equal(3, Feed.Recent.Count);
        Assert.Equal(2, Feed.UnreadCount);
    }

    [Fact]
    public async Task Does_not_connect_without_a_context()
    {
        await Feed.StartAsync();

        Assert.False(base.NotificationConnection.IsStarted);
        Assert.Empty(Feed.Recent);
    }

    [Fact]
    public async Task Context_change_calls_SwitchContext_on_the_open_connection()
    {
        await GrantAsync();
        await Feed.StartAsync();
        var next = Guid.NewGuid();

        await Context.SetAsync(next);

        await Eventually(() => base.NotificationConnection.Switched.Contains(next));
        Assert.Single(base.NotificationConnection.Switched);
    }

    [Fact]
    public async Task Logout_closes_the_connection()
    {
        await GrantAsync();
        await Feed.StartAsync();

        await Context.ClearAsync();

        await Eventually(() => base.NotificationConnection.StopCount == 1);
        Assert.False(base.NotificationConnection.IsStarted);
    }

    [Fact]
    public async Task Live_notification_is_added_and_counted_and_raises_Arrived()
    {
        await GrantAsync();
        await Feed.StartAsync();
        NotificationDto? arrived = null;
        Feed.Arrived += n => arrived = n;
        var live = Live();

        base.NotificationConnection.Push(live);

        Assert.Equal(live.Id, Feed.Recent[0].Id);
        Assert.Equal(3, Feed.UnreadCount);
        Assert.Equal(live.Id, arrived?.Id);
    }

    [Fact]
    public async Task Hub_failure_does_not_break_the_feed()
    {
        await GrantAsync();
        base.NotificationConnection.StartFailure = new HttpRequestException("hub down");

        await Feed.StartAsync();

        Assert.Equal(3, Feed.Recent.Count);
    }

    [Fact]
    public async Task Mark_read_lowers_the_unread_count()
    {
        await GrantAsync();
        await Feed.StartAsync();
        var unread = Feed.Recent.First(n => !n.IsRead);

        await Feed.MarkReadAsync(unread.Id);

        Assert.Equal(1, Feed.UnreadCount);
        Assert.Contains(NotificationsApi.Items, n => n.Id == unread.Id && n.IsRead);
    }
}
