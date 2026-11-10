using System.Net;
using Lms.Domain.Notifications;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Lms.Shared.Notifications;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Notifications;

/// <summary>Live pushes follow the active client: a person in clients A and B only hears about the one being worked in.</summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationPushScopeTests(LmsApiFactory factory)
{
    private static readonly Dictionary<string, string> Parameters = new()
    {
        ["RoleNameAr"] = "معلم", ["RoleNameEn"] = "Teacher", ["OrgUnitNameAr"] = "مدرسة", ["OrgUnitNameEn"] = "School",
    };

    private sealed class Listener(HubConnection connection) : IAsyncDisposable
    {
        private readonly List<NotificationDto> _received = [];

        public IReadOnlyList<NotificationDto> Received
        {
            get
            {
                lock (_received)
                {
                    return _received.ToArray();
                }
            }
        }

        public void Attach() => connection.On<NotificationDto>(NotificationHub.ReceiveMethod, dto =>
        {
            lock (_received)
            {
                _received.Add(dto);
            }
        });

        public async Task WaitForAsync(int count)
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            while (Received.Count < count && DateTime.UtcNow < until)
            {
                await Task.Delay(50);
            }
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private async Task<Listener> ConnectAsync(Guid user, Guid? assignment)
    {
        var url = NotificationHub.Path + (assignment is null ? string.Empty : $"?context={assignment}");
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, url), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers[TestAuthHandler.UserHeader] = user.ToString();
            })
            .Build();
        var listener = new Listener(connection);
        listener.Attach();
        await connection.StartAsync();
        // The handshake completes before the hub has joined the connection to its group; give that a moment (a push
        // sent in that instant would simply be found in the list, which the client loads on connect).
        await Task.Delay(400);
        return listener;
    }

    private async Task PublishAsync(Guid organizationId, Guid user)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<INotificationPublisher>().PublishAsync(
            new NotificationRequest(NotificationEvents.AccessRoleAssigned, organizationId, [user], Parameters));
    }

    [Fact]
    public async Task User_connected_in_client_A_receives_no_push_for_a_client_B_notification()
    {
        var a = await AccessTestData.SeedAsync(factory, "PS1A");
        var b = await AccessTestData.SeedAsync(factory, "PS1B");
        var user = Guid.NewGuid();
        var inA = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        var inB = await AccessTestData.AssignAsync(factory, b, b.School1, BuiltInRoles.Teacher, user);
        await using var inClientA = await ConnectAsync(user, inA.Id);

        await PublishAsync(b.Org.Id, user); // must not reach the connection of client A
        await PublishAsync(a.Org.Id, user); // proves the connection works at all

        await inClientA.WaitForAsync(1);
        await Task.Delay(500); // time for a wrongly delivered second push to show up
        var only = Assert.Single(inClientA.Received);
        Assert.Equal(NotificationEvents.AccessRoleAssigned, only.EventKey);

        // The B notification is not lost: it is in B's list, found by the id of the row.
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var rows = db.Set<Notification>().Where(x => x.UserId == user).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(only.Id, rows.Single(r => r.OrganizationId == a.Org.Id).Id);
        Assert.Equal(b.Org.Id, rows.Single(r => r.Id != only.Id).OrganizationId);
        Assert.NotEqual(Guid.Empty, inB.Id);
    }

    [Fact]
    public async Task Switching_context_by_reconnecting_receives_the_other_clients_pushes()
    {
        var a = await AccessTestData.SeedAsync(factory, "PS2A");
        var b = await AccessTestData.SeedAsync(factory, "PS2B");
        var user = Guid.NewGuid();
        var inA = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        var inB = await AccessTestData.AssignAsync(factory, b, b.School1, BuiltInRoles.Teacher, user);

        await using (var inClientA = await ConnectAsync(user, inA.Id))
        {
            await PublishAsync(a.Org.Id, user);
            await inClientA.WaitForAsync(1);
            Assert.Single(inClientA.Received);
        }

        await using var inClientB = await ConnectAsync(user, inB.Id);
        await PublishAsync(a.Org.Id, user);
        await PublishAsync(b.Org.Id, user);

        await inClientB.WaitForAsync(1);
        await Task.Delay(500);
        Assert.Single(inClientB.Received); // only the B one
    }

    [Fact]
    public async Task Two_connections_of_one_user_in_different_clients_each_get_only_their_own()
    {
        var a = await AccessTestData.SeedAsync(factory, "PS3A");
        var b = await AccessTestData.SeedAsync(factory, "PS3B");
        var user = Guid.NewGuid();
        var inA = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        var inB = await AccessTestData.AssignAsync(factory, b, b.School1, BuiltInRoles.Teacher, user);
        await using var tabA = await ConnectAsync(user, inA.Id);
        await using var tabB = await ConnectAsync(user, inB.Id);

        await PublishAsync(a.Org.Id, user);
        await PublishAsync(b.Org.Id, user);

        await tabA.WaitForAsync(1);
        await tabB.WaitForAsync(1);
        await Task.Delay(500);
        Assert.Single(tabA.Received);
        Assert.Single(tabB.Received);
        Assert.NotEqual(tabA.Received[0].Id, tabB.Received[0].Id);
    }

    [Fact]
    public async Task Other_users_in_the_same_client_get_nothing()
    {
        var a = await AccessTestData.SeedAsync(factory, "PS4");
        var user = Guid.NewGuid();
        var neighbour = Guid.NewGuid();
        var mine = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        var theirs = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, neighbour);
        await using var me = await ConnectAsync(user, mine.Id);
        await using var other = await ConnectAsync(neighbour, theirs.Id);

        await PublishAsync(a.Org.Id, user);

        await me.WaitForAsync(1);
        await Task.Delay(500);
        Assert.Single(me.Received);
        Assert.Empty(other.Received);
    }

    [Fact]
    public async Task A_connection_without_a_context_gets_no_client_notifications()
    {
        var a = await AccessTestData.SeedAsync(factory, "PS5");
        var user = Guid.NewGuid();
        await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        await using var noContext = await ConnectAsync(user, assignment: null);

        await PublishAsync(a.Org.Id, user);

        await Task.Delay(1000);
        Assert.Empty(noContext.Received);
    }

    [Fact]
    public async Task Connecting_with_another_users_assignment_is_refused()
    {
        var a = await AccessTestData.SeedAsync(factory, "PS6");
        var user = Guid.NewGuid();
        var stranger = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, Guid.NewGuid());

        var ex = await Assert.ThrowsAnyAsync<HttpRequestException>(() => ConnectAsync(user, stranger.Id));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }
}
