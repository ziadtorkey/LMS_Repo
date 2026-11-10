using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Notifications;
using Lms.Infrastructure;
using Lms.Infrastructure.Notifications;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Auth;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Notifications;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Notifications;

[Collection(ApiCollection.Name)]
public sealed class NotificationTests(LmsApiFactory factory)
{
    private static readonly Dictionary<string, string> RoleParameters = new()
    {
        ["RoleNameAr"] = "معلم", ["RoleNameEn"] = "Teacher", ["OrgUnitNameAr"] = "مدرسة", ["OrgUnitNameEn"] = "School",
    };

    private static readonly Dictionary<string, string> PolicyParameters = new()
    {
        ["PolicyKey"] = "CreateOrgUnits", ["OrgUnitNameAr"] = "مدرسة", ["OrgUnitNameEn"] = "School", ["State"] = "On",
    };

    private sealed class RecordingPusher : INotificationPusher
    {
        public List<(Guid UserId, NotificationDto Dto)> Pushed { get; } = [];

        public Task PushAsync(Guid userId, Guid? organizationId, NotificationDto notification, CancellationToken cancellationToken)
        {
            lock (Pushed)
            {
                Pushed.Add((userId, notification));
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>A publisher acting as the given caller, with its own recording pusher.</summary>
    private (NotificationPublisher Publisher, RecordingPusher Pusher) PublisherFor(TestCurrentContext actor)
    {
        var pusher = new RecordingPusher();
        var resolver = new NotificationRecipientResolver(factory.CreateDbContext(actor), new SystemClock());
        return (new NotificationPublisher(() => factory.CreateDbContext(actor), actor, resolver, pusher, new SystemClock()), pusher);
    }

    private HttpRequestMessage Get(string url, Guid user, Guid? assignment = null, string? method = null)
    {
        var request = new HttpRequestMessage(method is null ? HttpMethod.Get : new HttpMethod(method), url);
        request.Headers.Add(TestAuthHandler.UserHeader, user.ToString());
        if (assignment is not null)
        {
            request.Headers.Add(ContextHeader.Name, assignment.ToString());
        }

        return request;
    }

    private async Task<List<Notification>> RowsAsync(Guid userId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Notification>().Where(x => x.UserId == userId).OrderBy(x => x.CreatedAt).ToListAsync();
    }

    [Fact]
    public async Task Publish_stores_row_and_pushes_dto_over_signalr()
    {
        var tree = await AccessTestData.SeedAsync(factory, "N1");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);

        // A real hub connection for the user, over the in-memory server (long polling: no websockets in TestServer).
        var received = new TaskCompletionSource<NotificationDto>();
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, NotificationHub.Path + $"?context={assignment.Id}"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                options.Headers[TestAuthHandler.UserHeader] = user.ToString();
            })
            .Build();
        connection.On<NotificationDto>(NotificationHub.ReceiveMethod, dto => received.TrySetResult(dto));
        await connection.StartAsync();
        await connection.InvokeAsync("SwitchContext", assignment.Id); // barrier: the group join runs in OnConnectedAsync

        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<INotificationPublisher>().PublishAsync(new NotificationRequest(
            NotificationEvents.AccessRoleAssigned, tree.Org.Id, [user], RoleParameters, DeepLink: "/me"));

        var dto = await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(NotificationEvents.AccessRoleAssigned, dto.EventKey);
        Assert.Equal("Teacher", dto.Parameters["RoleNameEn"]);
        Assert.Equal("معلم", dto.Parameters["RoleNameAr"]);
        Assert.Equal("/me", dto.DeepLink);
        Assert.False(dto.IsRead);

        var row = Assert.Single(await RowsAsync(user));
        Assert.Equal(dto.Id, row.Id);
        Assert.Equal(tree.Org.Id, row.OrganizationId);
        Assert.Null(row.ReadAt);
        Assert.NotEqual(Guid.Empty, assignment.Id);
    }

    [Fact]
    public async Task Hub_rejects_anonymous_connection()
    {
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, NotificationHub.Path), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .Build();

        var ex = await Assert.ThrowsAnyAsync<HttpRequestException>(() => connection.StartAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task Hub_accepts_a_real_token_from_the_access_token_query_string_but_only_on_the_hub_path()
    {
        // The real OpenIddict validation (not the header test scheme): a WebSocket cannot send an Authorization header.
        var auth = new AuthTestHost(factory);
        var user = await auth.CreateUserAsync();
        var (access, _) = await auth.SignInForTokensAsync(user);
        var received = new TaskCompletionSource<NotificationDto>();
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(auth.Server.BaseAddress, NotificationHub.Path + "?access_token=" + Uri.EscapeDataString(access)), options =>
            {
                options.HttpMessageHandlerFactory = _ => auth.Server.CreateHandler();
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .Build();
        connection.On<NotificationDto>(NotificationHub.ReceiveMethod, dto => received.TrySetResult(dto));
        await connection.StartAsync();

        using (var scope = auth.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<INotificationPublisher>().PublishAsync(new NotificationRequest(
                NotificationEvents.AccessRoleAssigned, null, [user.Id], RoleParameters));
        }

        Assert.Equal(NotificationEvents.AccessRoleAssigned, (await received.Task.WaitAsync(TimeSpan.FromSeconds(15))).EventKey);

        // The same query-string token is NOT accepted anywhere else.
        using var api = auth.NewBrowser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("/api/auth/whoami?access_token=" + Uri.EscapeDataString(access))).StatusCode);
    }

    [Fact]
    public async Task Hub_rejects_an_invalid_token()
    {
        var auth = new AuthTestHost(factory);
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(auth.Server.BaseAddress, NotificationHub.Path + "?access_token=not-a-token"), options =>
            {
                options.HttpMessageHandlerFactory = _ => auth.Server.CreateHandler();
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .Build();

        var ex = await Assert.ThrowsAnyAsync<HttpRequestException>(() => connection.StartAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task User_sees_only_own_notifications()
    {
        var tree = await AccessTestData.SeedAsync(factory, "N2");
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        var mine = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, me);
        await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, other);
        var (publisher, _) = PublisherFor(TestCurrentContext.PlatformAdmin());
        await publisher.PublishAsync(new NotificationRequest(NotificationEvents.AccessRoleAssigned, tree.Org.Id, [me], RoleParameters));
        await publisher.PublishAsync(new NotificationRequest(NotificationEvents.AccessRoleAssigned, tree.Org.Id, [other], RoleParameters));

        var response = await factory.CreateClient().SendAsync(Get(Routes.MyNotifications, me, mine.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<NotificationPageDto>())!;
        var item = Assert.Single(page.Items);
        Assert.Equal(1, page.Total);
        Assert.Equal(1, page.UnreadCount);
        Assert.Equal("Teacher", item.Parameters["RoleNameEn"]);
        Assert.Equal((await RowsAsync(me)).Single().Id, item.Id);
    }

    [Fact]
    public async Task Mark_read_of_other_users_notification_returns_404()
    {
        var tree = await AccessTestData.SeedAsync(factory, "N3");
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        var mine = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, me);
        var (publisher, _) = PublisherFor(TestCurrentContext.PlatformAdmin());
        await publisher.PublishAsync(new NotificationRequest(NotificationEvents.AccessRoleAssigned, tree.Org.Id, [other], RoleParameters));
        var otherRow = (await RowsAsync(other)).Single();
        var client = factory.CreateClient();

        var response = await client.SendAsync(Get(Routes.MyNotificationRead(otherRow.Id), me, mine.Id, "POST"));
        var unknown = await client.SendAsync(Get(Routes.MyNotificationRead(Guid.NewGuid()), me, mine.Id, "POST"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Null((await RowsAsync(other)).Single().ReadAt);
    }

    [Fact]
    public async Task Unread_count_and_read_all()
    {
        var tree = await AccessTestData.SeedAsync(factory, "N4");
        var me = Guid.NewGuid();
        var mine = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, me);
        var (publisher, _) = PublisherFor(TestCurrentContext.PlatformAdmin());
        for (var i = 0; i < 3; i++)
        {
            await publisher.PublishAsync(new NotificationRequest(NotificationEvents.AccessRoleAssigned, tree.Org.Id, [me], RoleParameters));
        }

        var client = factory.CreateClient();
        var before = (await (await client.SendAsync(Get(Routes.MyNotifications, me, mine.Id))).Content.ReadFromJsonAsync<NotificationPageDto>())!;
        Assert.Equal(3, before.UnreadCount);

        var first = before.Items[0];
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Get(Routes.MyNotificationRead(first.Id), me, mine.Id, "POST"))).StatusCode);
        var afterOne = (await (await client.SendAsync(Get(Routes.MyNotifications, me, mine.Id))).Content.ReadFromJsonAsync<NotificationPageDto>())!;
        Assert.Equal(2, afterOne.UnreadCount);
        Assert.Equal(3, afterOne.Total);
        Assert.Equal(2, (await (await client.SendAsync(Get(Routes.MyNotifications + "?unreadOnly=true", me, mine.Id)))
            .Content.ReadFromJsonAsync<NotificationPageDto>())!.Items.Count);

        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(Get(Routes.MyNotificationsReadAll, me, mine.Id, "POST"))).StatusCode);
        var afterAll = (await (await client.SendAsync(Get(Routes.MyNotifications, me, mine.Id))).Content.ReadFromJsonAsync<NotificationPageDto>())!;
        Assert.Equal(0, afterAll.UnreadCount);
        Assert.All(afterAll.Items, x => Assert.True(x.IsRead));
        Assert.Empty((await (await client.SendAsync(Get(Routes.MyNotifications + "?unreadOnly=true", me, mine.Id)))
            .Content.ReadFromJsonAsync<NotificationPageDto>())!.Items);
    }

    [Fact]
    public async Task Read_all_does_not_touch_other_users_rows()
    {
        var tree = await AccessTestData.SeedAsync(factory, "N5");
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        var mine = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, me);
        var (publisher, _) = PublisherFor(TestCurrentContext.PlatformAdmin());
        await publisher.PublishAsync(new NotificationRequest(NotificationEvents.AccessRoleAssigned, tree.Org.Id, [me, other], RoleParameters));

        await factory.CreateClient().SendAsync(Get(Routes.MyNotificationsReadAll, me, mine.Id, "POST"));

        Assert.NotNull((await RowsAsync(me)).Single().ReadAt);
        Assert.Null((await RowsAsync(other)).Single().ReadAt);
    }

    [Fact]
    public async Task Paging_is_validated_and_applied()
    {
        var tree = await AccessTestData.SeedAsync(factory, "N6");
        var me = Guid.NewGuid();
        var mine = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, me);
        var (publisher, _) = PublisherFor(TestCurrentContext.PlatformAdmin());
        for (var i = 0; i < 5; i++)
        {
            await publisher.PublishAsync(new NotificationRequest(NotificationEvents.AccessRoleAssigned, tree.Org.Id, [me], RoleParameters));
        }

        var client = factory.CreateClient();
        var page2 = (await (await client.SendAsync(Get(Routes.MyNotifications + "?page=2&pageSize=2", me, mine.Id)))
            .Content.ReadFromJsonAsync<NotificationPageDto>())!;

        Assert.Equal(2, page2.Items.Count);
        Assert.Equal(5, page2.Total);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Get(Routes.MyNotifications + "?page=0", me, mine.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(Get(Routes.MyNotifications + "?pageSize=500", me, mine.Id))).StatusCode);
    }

    [Fact]
    public async Task Notification_endpoints_require_authentication()
    {
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Routes.MyNotifications)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(Routes.MyNotificationsReadAll, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(Routes.MyNotificationRead(Guid.NewGuid()), null)).StatusCode);
    }

    [Fact]
    public async Task Role_based_event_reaches_managers_at_and_below_unit_but_not_actor_or_sibling()
    {
        var tree = await AccessTestData.SeedAsync(factory, "N7");
        var managerAtUnit = Guid.NewGuid();
        var managerBelow = Guid.NewGuid();
        var orgAdminAtUnit = Guid.NewGuid();
        var teacherAtUnit = Guid.NewGuid();
        var managerSibling = Guid.NewGuid();
        var actorManager = Guid.NewGuid();
        var managerAbove = Guid.NewGuid();
        var expired = Guid.NewGuid();
        await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Manager, managerAtUnit);
        await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, orgAdminAtUnit);
        await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, teacherAtUnit);
        await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Manager, actorManager);
        await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, managerAbove);
        await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Manager, expired,
            validTo: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        // A unit below School1 and a sibling School2, each holding a manager.
        Lms.Domain.Organizations.OrgUnit section;
        Lms.Domain.Organizations.OrgUnit school2;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var levelId = db.LevelTypes.Single(x => x.OrganizationId == tree.Org.Id).Id;
            section = Lms.Domain.Organizations.OrgUnit.CreateChild(tree.School1, levelId, "فصل", "Section", "SEC");
            school2 = Lms.Domain.Organizations.OrgUnit.CreateChild(tree.Root, levelId, "مدرسة 2", "School2", "S2");
            db.AddRange(section, school2);
            await db.SaveChangesAsync();
        }

        await AccessTestData.AssignAsync(factory, tree, section, BuiltInRoles.Manager, managerBelow);
        await AccessTestData.AssignAsync(factory, tree, school2, BuiltInRoles.Manager, managerSibling);
        var (publisher, pusher) = PublisherFor(
            new TestCurrentContext { UserId = actorManager, OrganizationId = tree.Org.Id, ScopePath = tree.Root.Path });

        await publisher.PublishAsync(new NotificationRequest(
            NotificationEvents.OrganizationPolicyChanged, tree.Org.Id, [], PolicyParameters, OrgUnitPath: tree.School1.Path));

        var recipients = pusher.Pushed.Select(p => p.UserId).ToHashSet();
        Assert.Equal(new[] { managerAtUnit, managerBelow, orgAdminAtUnit }.Order(), recipients.Order());
        Assert.DoesNotContain(actorManager, recipients);
        Assert.DoesNotContain(managerSibling, recipients);
        Assert.DoesNotContain(managerAbove, recipients);
        Assert.DoesNotContain(teacherAtUnit, recipients);
        Assert.DoesNotContain(expired, recipients);
    }

    [Fact]
    public async Task Publisher_never_notifies_the_acting_user_even_when_named()
    {
        var tree = await AccessTestData.SeedAsync(factory, "N8");
        var actor = Guid.NewGuid();
        var other = Guid.NewGuid();
        var (publisher, pusher) = PublisherFor(new TestCurrentContext { UserId = actor, OrganizationId = tree.Org.Id, ScopePath = tree.Root.Path });

        await publisher.PublishAsync(new NotificationRequest(NotificationEvents.AccessRoleAssigned, tree.Org.Id, [actor, other, other], RoleParameters));

        Assert.Equal([other], pusher.Pushed.Select(p => p.UserId));
        Assert.Empty(await RowsAsync(actor));
        Assert.Single(await RowsAsync(other));
    }

    [Fact]
    public async Task Unknown_event_or_missing_parameter_throws()
    {
        var tree = await AccessTestData.SeedAsync(factory, "N9");
        var (publisher, pusher) = PublisherFor(TestCurrentContext.PlatformAdmin());
        var user = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            publisher.PublishAsync(new NotificationRequest("Nope.Unknown", tree.Org.Id, [user], RoleParameters)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            publisher.PublishAsync(new NotificationRequest(NotificationEvents.AccessRoleAssigned.ToLowerInvariant(), tree.Org.Id, [user], RoleParameters)));
        var missing = new Dictionary<string, string>(RoleParameters);
        missing.Remove("RoleNameEn");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            publisher.PublishAsync(new NotificationRequest(NotificationEvents.AccessRoleAssigned, tree.Org.Id, [user], missing)));
        // No recipients and no unit to resolve them from.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            publisher.PublishAsync(new NotificationRequest(NotificationEvents.AccessRoleAssigned, tree.Org.Id, [], RoleParameters)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            publisher.PublishAsync(new NotificationRequest(NotificationEvents.OrganizationPolicyChanged, tree.Org.Id, [], PolicyParameters)));

        Assert.Empty(pusher.Pushed);
        Assert.Empty(await RowsAsync(user));
    }

    [Theory]
    [InlineData("//evil.example/x")]
    [InlineData("https://evil.example/x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("\\\\evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("relative/path")]
    [InlineData("")]
    public async Task DeepLink_must_be_an_app_relative_path(string deepLink)
    {
        var tree = await AccessTestData.SeedAsync(factory, "N10");
        var (publisher, pusher) = PublisherFor(TestCurrentContext.PlatformAdmin());
        var user = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() => publisher.PublishAsync(
            new NotificationRequest(NotificationEvents.AccessRoleAssigned, tree.Org.Id, [user], RoleParameters, deepLink)));

        Assert.Empty(pusher.Pushed);
        Assert.Empty(await RowsAsync(user));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/org-units/123?tab=roles")]
    public async Task DeepLink_with_an_app_relative_path_is_accepted(string deepLink)
    {
        var tree = await AccessTestData.SeedAsync(factory, "N11");
        var (publisher, _) = PublisherFor(TestCurrentContext.PlatformAdmin());
        var user = Guid.NewGuid();

        await publisher.PublishAsync(new NotificationRequest(NotificationEvents.AccessRoleAssigned, tree.Org.Id, [user], RoleParameters, deepLink));

        Assert.Equal(deepLink, (await RowsAsync(user)).Single().DeepLink);
    }
}
