using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.Domain.Notifications;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;

namespace Lms.IntegrationTests.Messaging;

/// <summary>The messaging realtime events (ids only, participants only) and the throttled message notifications (M6 Task 12, L13, FR-NOT-02).</summary>
[Collection(ApiCollection.Name)]
public sealed class MessagingRealtimeTests(LmsApiFactory factory)
{
    private sealed record Setup(AccessTree Tree, RoleAssignment A, RoleAssignment B, RoleAssignment C, RoleAssignment Outsider, Guid ConversationId);

    private sealed class Listener : IAsyncDisposable
    {
        public HubConnection Connection { get; }
        public List<MessageReceivedEvent> Received { get; } = [];
        public List<ConversationReadEvent> Read { get; } = [];
        public List<ConversationChangedEvent> Changed { get; } = [];

        public Listener(LmsApiFactory factory, RoleAssignment who)
        {
            Connection = new HubConnectionBuilder()
                .WithUrl(new Uri(factory.Server.BaseAddress, NotificationHub.Path + $"?context={who.Id}"), options =>
                {
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    options.Transports = HttpTransportType.LongPolling;
                    options.Headers[TestAuthHandler.UserHeader] = who.UserId.ToString();
                })
                .Build();
            Connection.On<MessageReceivedEvent>(NotificationHub.MessageReceivedMethod, e => { lock (Received) { Received.Add(e); } });
            Connection.On<ConversationReadEvent>(NotificationHub.ConversationReadMethod, e => { lock (Read) { Read.Add(e); } });
            Connection.On<ConversationChangedEvent>(NotificationHub.ConversationChangedMethod, e => { lock (Changed) { Changed.Add(e); } });
        }

        /// <summary>The handshake can finish before the group join; one refused call is a barrier (hub calls run after OnConnectedAsync).</summary>
        public async Task StartAsync()
        {
            await Connection.StartAsync();
            await Assert.ThrowsAsync<HubException>(() => Connection.InvokeAsync("SwitchContext", Guid.NewGuid()));
        }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    private async Task<Setup> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        using (var scope = new EngineScope(factory.Services))
        {
            await scope.Settings.SetAsync(FeatureFlags.Messaging, tree.Root.Id, true, false);
        }

        async Task<RoleAssignment> StaffAsync(string role) =>
            await AccessTestData.AssignAsync(factory, tree, tree.School1, role, (await PeopleTestKit.AddStaffAsync(factory, tree.School1)).UserId);

        var a = await StaffAsync(BuiltInRoles.Teacher);
        var b = await StaffAsync(BuiltInRoles.Counselor);
        var c = await StaffAsync(BuiltInRoles.Teacher);
        var outsider = await StaffAsync(BuiltInRoles.Teacher);
        var created = await SendAsync(factory, HttpMethod.Post, Routes.Conversations, a, new CreateConversationRequest(ConversationKinds.Direct, [b.UserId], null, null));
        return new Setup(tree, a, b, c, outsider, (await created.Content.ReadFromJsonAsync<ConversationDto>(Web))!.Id);
    }

    private async Task<MessageDto> PostAsync(RoleAssignment who, Guid conversationId, string text)
    {
        var response = await SendAsync(factory, HttpMethod.Post, Routes.ConversationMessages(conversationId), who, new SendMessageRequest(text, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MessageDto>(Web))!;
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(100);
        }
    }

    private async Task<List<Notification>> NewMessageNoticesAsync(Guid userId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Notification>().Where(n => n.UserId == userId && n.EventKey == NotificationEvents.MessagesNewMessage).ToListAsync();
    }

    [Fact]
    public async Task A_new_message_reaches_the_other_participant_only_and_carries_ids_only()
    {
        var s = await SetUpAsync("RT1");
        await using var sender = new Listener(factory, s.A);
        await using var receiver = new Listener(factory, s.B);
        await using var stranger = new Listener(factory, s.Outsider);
        await sender.StartAsync();
        await receiver.StartAsync();
        await stranger.StartAsync();

        var sent = await PostAsync(s.A, s.ConversationId, "private words that must not travel");

        await WaitAsync(() => receiver.Received.Count >= 1);
        Assert.Equal(new MessageReceivedEvent(s.ConversationId, sent.Id), Assert.Single(receiver.Received));
        await Task.Delay(500);
        Assert.Empty(sender.Received); // the sender does not need an event for their own message
        Assert.Empty(stranger.Received);
        Assert.DoesNotContain(typeof(MessageReceivedEvent).GetProperties(), p => p.PropertyType == typeof(string)); // ids only: the event type has no text at all
    }

    [Fact]
    public async Task Reading_tells_the_readers_own_connections_and_nobody_else()
    {
        var s = await SetUpAsync("RT2");
        var sent = await PostAsync(s.A, s.ConversationId, "hello");
        await using var reader = new Listener(factory, s.B);
        await using var other = new Listener(factory, s.A);
        await reader.StartAsync();
        await other.StartAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Post, Routes.ConversationRead(s.ConversationId), s.B)).StatusCode);

        await WaitAsync(() => reader.Read.Count >= 1);
        Assert.Equal(new ConversationReadEvent(s.ConversationId, sent.Id), Assert.Single(reader.Read));
        await Task.Delay(500);
        Assert.Empty(other.Read);
    }

    [Fact]
    public async Task A_member_who_left_gets_no_more_events_and_members_hear_about_changes()
    {
        var s = await SetUpAsync("RT3");
        var created = await SendAsync(factory, HttpMethod.Post, Routes.Conversations, s.A, new CreateConversationRequest(ConversationKinds.Group, [s.B.UserId, s.C.UserId], "Team", null));
        var group = (await created.Content.ReadFromJsonAsync<ConversationDto>(Web))!;
        await using var leaving = new Listener(factory, s.B);
        await using var staying = new Listener(factory, s.C);
        await leaving.StartAsync();
        await staying.StartAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Post, Routes.ConversationLeave(group.Id), s.B)).StatusCode);
        await WaitAsync(() => leaving.Changed.Count >= 1 && staying.Changed.Count >= 1); // both refetch the member list
        Assert.Equal(new ConversationChangedEvent(group.Id), Assert.Single(staying.Changed));

        await PostAsync(s.A, group.Id, "after b left");
        await WaitAsync(() => staying.Received.Count >= 1);
        await Task.Delay(500);
        Assert.Single(staying.Received);
        Assert.Empty(leaving.Received);
    }

    [Fact]
    public async Task One_unread_notification_per_conversation_until_it_is_read_and_it_names_the_sender_only()
    {
        var s = await SetUpAsync("RT4");

        await PostAsync(s.A, s.ConversationId, "first secret body");
        await PostAsync(s.A, s.ConversationId, "second secret body");
        await PostAsync(s.A, s.ConversationId, "third secret body");

        var notices = await NewMessageNoticesAsync(s.B.UserId);
        var one = Assert.Single(notices);
        Assert.DoesNotContain("secret", one.ParametersJson); // the sender name only, never the text
        Assert.Contains("SenderNameEn", one.ParametersJson);
        Assert.Equal("/messages/" + s.ConversationId, one.DeepLink);
        Assert.Empty(await NewMessageNoticesAsync(s.A.UserId)); // never the sender

        await SendAsync(factory, HttpMethod.Post, Routes.ConversationRead(s.ConversationId), s.B);
        await PostAsync(s.A, s.ConversationId, "after reading");

        var after = await NewMessageNoticesAsync(s.B.UserId);
        Assert.Equal(2, after.Count);
        Assert.Single(after, n => n.ReadAt is null);
    }

    [Fact]
    public async Task A_muted_conversation_sends_no_notification()
    {
        var s = await SetUpAsync("RT5");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(factory, HttpMethod.Put, Routes.ConversationMute(s.ConversationId), s.B, new MuteConversationRequest(null))).StatusCode);

        await PostAsync(s.A, s.ConversationId, "quiet please");

        Assert.Empty(await NewMessageNoticesAsync(s.B.UserId));
    }
}
