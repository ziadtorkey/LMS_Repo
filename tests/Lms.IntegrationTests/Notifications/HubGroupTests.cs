using Lms.Domain.Notifications;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Lms.Shared.Notifications;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Notifications;

/// <summary>Live pushes go to the group of the active client only (a user in two clients never sees the other's toast).</summary>
[Collection(ApiCollection.Name)]
public sealed class HubGroupTests(LmsApiFactory factory)
{
    private static readonly Dictionary<string, string> Parameters = new()
    {
        ["RoleNameAr"] = "معلم", ["RoleNameEn"] = "Teacher", ["OrgUnitNameAr"] = "مدرسة", ["OrgUnitNameEn"] = "School",
    };

    private sealed class Listener : IAsyncDisposable
    {
        public HubConnection Connection { get; }
        public List<NotificationDto> Received { get; } = [];

        private readonly Guid? _assignmentId;

        public Listener(LmsApiFactory factory, Guid user, Guid? assignmentId)
        {
            _assignmentId = assignmentId;
            var query = assignmentId is null ? string.Empty : $"?context={assignmentId}";
            Connection = new HubConnectionBuilder()
                .WithUrl(new Uri(factory.Server.BaseAddress, NotificationHub.Path + query), options =>
                {
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    options.Transports = HttpTransportType.LongPolling;
                    options.Headers[TestAuthHandler.UserHeader] = user.ToString();
                })
                .Build();
            Connection.On<NotificationDto>(NotificationHub.ReceiveMethod, dto =>
            {
                lock (Received)
                {
                    Received.Add(dto);
                }
            });
        }

        /// <summary>
        /// Starts the connection and waits until the server has finished joining its group: the handshake can complete
        /// before OnConnectedAsync does, and hub invocations run only after it, so one round trip is a barrier.
        /// </summary>
        public async Task StartAsync()
        {
            await Connection.StartAsync();
            if (_assignmentId is { } id)
            {
                await Connection.InvokeAsync("SwitchContext", id);
            }
        }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    private async Task PublishAsync(Guid organizationId, Guid user)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<INotificationPublisher>().PublishAsync(new NotificationRequest(
            NotificationEvents.AccessRoleAssigned, organizationId, [user], Parameters, DeepLink: "/me"));
    }

    private static async Task WaitForAsync(Listener listener, int count)
    {
        for (var i = 0; i < 150 && listener.Received.Count < count; i++)
        {
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task User_connected_in_client_A_gets_no_push_for_client_B_but_B_still_lists_it()
    {
        var a = await AccessTestData.SeedAsync(factory, "HG1");
        var b = await AccessTestData.SeedAsync(factory, "HG2");
        var user = Guid.NewGuid();
        var inA = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        var inB = await AccessTestData.AssignAsync(factory, b, b.School1, BuiltInRoles.Teacher, user);
        await using var connectedInA = new Listener(factory, user, inA.Id);
        await using var connectedInB = new Listener(factory, user, inB.Id);
        await connectedInA.StartAsync();
        await connectedInB.StartAsync();

        await PublishAsync(b.Org.Id, user);

        await WaitForAsync(connectedInB, 1);
        Assert.Single(connectedInB.Received);
        await Task.Delay(500);
        Assert.Empty(connectedInA.Received);

        // The B notification is in B's list: its stored row belongs to B.
        await using var db = factory.CreateDbContext(TestCurrentContext.For(b.Org.Id, b.Root.Path));
        Assert.Single(db.Set<Notification>().Where(n => n.UserId == user).ToList());
    }

    [Fact]
    public async Task Switching_context_rejoins_the_right_group()
    {
        var a = await AccessTestData.SeedAsync(factory, "HG3");
        var b = await AccessTestData.SeedAsync(factory, "HG4");
        var user = Guid.NewGuid();
        var inA = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        var inB = await AccessTestData.AssignAsync(factory, b, b.School1, BuiltInRoles.Teacher, user);
        await using var listener = new Listener(factory, user, inA.Id);
        await listener.StartAsync();

        await listener.Connection.InvokeAsync("SwitchContext", inB.Id);
        await PublishAsync(a.Org.Id, user);
        await PublishAsync(b.Org.Id, user);

        await WaitForAsync(listener, 1);
        await Task.Delay(500);
        var only = Assert.Single(listener.Received);
        Assert.Equal(NotificationEvents.AccessRoleAssigned, only.EventKey);
    }

    [Fact]
    public async Task Switching_to_someone_elses_assignment_is_refused()
    {
        var a = await AccessTestData.SeedAsync(factory, "HG5");
        var user = Guid.NewGuid();
        var mine = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        var theirs = await AccessTestData.AssignAsync(factory, a, a.School2, BuiltInRoles.Teacher, Guid.NewGuid());
        await using var listener = new Listener(factory, user, mine.Id);
        await listener.StartAsync();

        await Assert.ThrowsAsync<HubException>(() => listener.Connection.InvokeAsync("SwitchContext", theirs.Id));
        await Assert.ThrowsAsync<HubException>(() => listener.Connection.InvokeAsync("SwitchContext", Guid.NewGuid()));
    }

    [Fact]
    public async Task Connection_without_a_context_receives_no_client_notifications()
    {
        var a = await AccessTestData.SeedAsync(factory, "HG6");
        var user = Guid.NewGuid();
        await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        await using var listener = new Listener(factory, user, null);
        await listener.StartAsync();

        await PublishAsync(a.Org.Id, user);

        await Task.Delay(1000);
        Assert.Empty(listener.Received);
    }
}
