using Bunit;
using Lms.Client.Components.Messaging;
using Lms.Client.Services.Messaging;
using Lms.Client.Tests.Support;
using Lms.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Messaging;

/// <summary>Realtime messaging (M6 Task 23, FR-MSG-01, FR-NOT-02, X-07) against the fake hub: ids only, read before shown, once per id, in the server order, resync after a reconnect.</summary>
public sealed class MessagingRealtimeTests : ClientTestContext
{
    private static readonly DateTime T0 = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
    private readonly FakeMessagingApi _api = new() { Now = () => T0 };
    private readonly FakeMessagingHub _hub = new();
    private readonly MessagingRealtime _realtime;

    public MessagingRealtimeTests()
    {
        _realtime = new MessagingRealtime(_hub, _api);
        Services.AddSingleton(_realtime);
    }

    private async Task<ConversationDto> ConversationAsync() =>
        await _api.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Direct, [FakeMessagingApi.Huda.Id], null, "hello"));

    private static async Task<T> NextAsync<T>(Action<Action<T>> subscribe, Action trigger)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        subscribe(v => tcs.TrySetResult(v));
        trigger();
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Event_carries_ids_only_and_the_message_is_fetched_before_it_is_shown()
    {
        var c = await ConversationAsync();
        var sent = _api.Receive(c.Id, FakeMessagingApi.Huda, "the text is not in the event");
        _realtime.OpenConversationId = c.Id;

        var (conversation, message) = await NextAsync<(Guid, MessageDto)>(h => _realtime.MessageArrived += (a, b) => h((a, b)), () => _hub.Receive(c.Id, sent.Id));

        Assert.Equal(c.Id, conversation);
        Assert.Equal("the text is not in the event", message.Body); // read through the API, not carried by the event
    }

    [Fact]
    public async Task A_message_the_api_does_not_return_is_never_shown_from_the_event_alone()
    {
        var c = await ConversationAsync();
        _realtime.OpenConversationId = c.Id;
        var shown = false;
        _realtime.MessageArrived += (_, _) => shown = true;

        _hub.Receive(c.Id, Guid.NewGuid()); // an id the caller cannot read
        await Task.Delay(150);

        Assert.False(shown);
    }

    [Fact]
    public async Task Incoming_message_for_another_thread_updates_unread_and_announces_once()
    {
        var other = Guid.NewGuid();
        var calls = new List<bool>();
        _realtime.OtherThreadMessage += (_, announce) => calls.Add(announce);
        _realtime.OpenConversationId = Guid.NewGuid();

        _hub.Receive(other, Guid.NewGuid());
        _hub.Receive(other, Guid.NewGuid());
        await Task.Delay(50);
        Assert.Equal([true, false], calls); // two messages, one announcement

        _realtime.MarkRead(other);
        _hub.Receive(other, Guid.NewGuid());
        await Task.Delay(50);
        Assert.Equal([true, false, true], calls); // after it was read the next one is announced again
    }

    [Fact]
    public async Task Duplicate_message_ids_are_ignored()
    {
        var c = await ConversationAsync();
        var sent = _api.Receive(c.Id, FakeMessagingApi.Huda, "once");
        _realtime.OpenConversationId = c.Id;
        var count = 0;
        _realtime.MessageArrived += (_, _) => count++;

        _hub.Receive(c.Id, sent.Id);
        _hub.Receive(c.Id, sent.Id);
        await Task.Delay(200);

        Assert.Equal(1, count);
    }

    [Fact]
    public void Ordering_is_by_server_time_and_id_not_arrival_time()
    {
        MessageDto Make(string body, int minute, Guid? id = null) =>
            new(id ?? Guid.NewGuid(), Guid.NewGuid(), null, "م", "S", body, MessageStates.Normal, [], T0.AddMinutes(minute), null, false);
        var first = Make("first", 1);
        var second = Make("second", 2);
        var third = Make("third", 3);

        // the second arrives last, the first and the third arrived earlier: the merged list still follows the server
        var merged = MessagingRealtime.Merge([third], [first, second, third]);

        Assert.Equal(["first", "second", "third"], merged.Select(m => m.Body));
        Assert.Equal(3, merged.Count); // no id twice
    }

    [Fact]
    public async Task Reconnect_refetches_and_merges_by_id()
    {
        var c = await ConversationAsync();
        _realtime.OpenConversationId = c.Id;
        var missed = _api.Receive(c.Id, FakeMessagingApi.Huda, "arrived while offline"); // no event reached the client

        _hub.Drop();
        var (open, latest) = await NextAsync<(Guid?, IReadOnlyList<MessageDto>)>(h => _realtime.Resynced += (a, b) => h((a, b)), () => _hub.Reconnect());

        Assert.Equal(c.Id, open);
        Assert.Contains(latest, m => m.Id == missed.Id);
        var shownBefore = new List<MessageDto> { latest[0] };
        Assert.Equal(latest.Count, MessagingRealtime.Merge(shownBefore, latest).Count); // merging the refetch into what is on screen adds no duplicates
    }

    [Fact]
    public async Task Context_switch_clears_and_reloads_the_inbox_and_forgets_seen_ids()
    {
        var c = await ConversationAsync();
        var sent = _api.Receive(c.Id, FakeMessagingApi.Huda, "hi");
        _realtime.OpenConversationId = c.Id;
        var count = 0;
        _realtime.MessageArrived += (_, _) => count++;
        _hub.Receive(c.Id, sent.Id);
        await Task.Delay(100);
        var reset = false;
        _realtime.ContextReset += () => reset = true;

        var newContext = Guid.NewGuid();
        await _realtime.SwitchContextAsync(newContext);

        Assert.True(reset);
        Assert.Equal([newContext], _hub.Switched);
        Assert.Null(_realtime.OpenConversationId); // the open thread belonged to the old context
        _realtime.OpenConversationId = c.Id;
        _hub.Receive(c.Id, sent.Id);
        await Task.Delay(100);
        Assert.Equal(2, count); // the id is new to the new context's list
    }

    [Fact]
    public void Conversation_changed_reloads_members_and_state()
    {
        var changed = Guid.Empty;
        _realtime.ConversationChanged += id => changed = id;
        var id = Guid.NewGuid();

        _hub.Change(id);

        Assert.Equal(id, changed);
    }

    [Fact]
    public async Task Offline_banner_shows_while_disconnected()
    {
        FakeCultureService.Apply(new System.Globalization.CultureInfo("en"));
        await _hub.StartAsync(Guid.NewGuid());
        var banner = Render<RealtimeBanner>();
        Assert.Empty(banner.FindAll("[data-testid=realtime-offline]"));

        _hub.Drop();
        banner.WaitForElement("[data-testid=realtime-offline]");
        Assert.Equal("status", banner.Find("[data-testid=realtime-offline]").GetAttribute("role")); // polite, not an alert

        _hub.Reconnect();
        banner.WaitForAssertion(() => Assert.Empty(banner.FindAll("[data-testid=realtime-offline]")));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 5)]
    [InlineData(3, 10)]
    [InlineData(4, 30)]
    [InlineData(5, 60)]
    [InlineData(20, 60)]
    public void Backoff_grows_then_settles_at_a_minute_and_never_gives_up(int previous, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RealtimeBackoff.Next(previous));
    }

    [Fact]
    public void Hub_binding_uses_the_contract_method_names_and_the_backoff_policy()
    {
        Assert.Equal("MessageReceived", Lms.Shared.Notifications.NotificationHub.MessageReceivedMethod);
        Assert.Equal("ConversationChanged", Lms.Shared.Notifications.NotificationHub.ConversationChangedMethod);
        var policy = new Lms.Client.Services.HubRetryPolicy();
        Assert.Equal(TimeSpan.Zero, policy.NextRetryDelay(new Microsoft.AspNetCore.SignalR.Client.RetryContext { PreviousRetryCount = 0 }));
        Assert.Equal(TimeSpan.FromMinutes(1), policy.NextRetryDelay(new Microsoft.AspNetCore.SignalR.Client.RetryContext { PreviousRetryCount = 9 }));
    }

    [Fact]
    public async Task A_change_of_context_in_the_store_resets_the_realtime_state()
    {
        var store = Services.GetRequiredService<Lms.Client.Services.IContextStore>();
        var realtime = new MessagingRealtime(_hub, _api, store);
        realtime.OpenConversationId = Guid.NewGuid();
        var reset = false;
        realtime.ContextReset += () => reset = true;

        await store.SetAsync(Guid.NewGuid());

        Assert.True(reset);
        Assert.Null(realtime.OpenConversationId);
    }
}
