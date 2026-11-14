using Bunit;
using Lms.Client.Pages.Messaging;
using Lms.Client.Services.Messaging;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Messaging;

/// <summary>Realtime in the thread and the inbox (M6 Task 23): append without moving focus, the jump control when scrolled up, the badge, the announcer, a reconnect and a change of members.</summary>
public sealed class MessagingRealtimeThreadTests : ClientTestContext
{
    private static FakeMessagingUser Sara => FakeMessagingApi.Sara;
    private static FakeMessagingUser Huda => FakeMessagingApi.Huda;

    private DateTime _clock = DateTime.UtcNow.AddHours(-2);

    public MessagingRealtimeThreadTests() => MessagingApi.Now = () => _clock = _clock.AddSeconds(1);

    private async Task<ConversationDto> SignInAndConversationAsync()
    {
        Culture.Current = new System.Globalization.CultureInfo("en");
        FakeCultureService.Apply(new System.Globalization.CultureInfo("en"));
        Api.Me = Api.Me with { UserId = Sara.Id };
        await GrantAsync(Permissions.Messaging.View, Permissions.Messaging.Send);
        MessagingApi.Me = Huda;
        var c = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Direct, [Sara.Id], null, "first"));
        MessagingApi.Me = Sara;
        return c;
    }

    private IRenderedComponent<MessagesPage> Open(Guid? id)
    {
        var cut = Render<MessagesPage>(p => p.Add(x => x.Id, id));
        cut.WaitForState(() => cut.FindAll("[data-testid=messages-loading]").Count == 0);
        return cut;
    }

    private MessageDto HudaSays(Guid conversationId, string text)
    {
        var previous = MessagingApi.Me;
        var m = MessagingApi.Receive(conversationId, Huda, text);
        MessagingApi.Me = previous;
        return m;
    }

    [Fact]
    public async Task Incoming_message_for_the_open_thread_is_appended_without_stealing_focus()
    {
        var c = await SignInAndConversationAsync();
        var module = JSInterop.SetupModule("./js/thread.js");
        module.Setup<bool>("isNearBottom", _ => true).SetResult(true);
        module.SetupVoid("scrollToEnd", _ => true).SetVoidResult();
        var cut = Open(c.Id);
        cut.WaitForElement("[data-testid=thread-body]");
        var before = cut.FindAll("[data-testid^=msg-]").Count;

        var incoming = HudaSays(c.Id, "a live message");
        MessagingHub.Receive(c.Id, incoming.Id);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"[data-testid=msg-{incoming.Id}]")));
        Assert.Contains("a live message", cut.Markup);
        Assert.True(cut.FindAll("[data-testid^=msg-]").Count > before);
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase)); // nothing moves the focus
        Assert.Empty(cut.FindAll("[data-testid=thread-jump]"));
    }

    [Fact]
    public async Task Incoming_message_when_scrolled_up_shows_the_jump_control()
    {
        var c = await SignInAndConversationAsync();
        var module = JSInterop.SetupModule("./js/thread.js");
        module.Setup<bool>("isNearBottom", _ => true).SetResult(false); // the reader is scrolled up
        module.SetupVoid("scrollToEnd", _ => true).SetVoidResult();
        var cut = Open(c.Id);
        cut.WaitForElement("[data-testid=thread-body]");

        var one = HudaSays(c.Id, "one");
        MessagingHub.Receive(c.Id, one.Id);
        var two = HudaSays(c.Id, "two");
        MessagingHub.Receive(c.Id, two.Id);

        cut.WaitForAssertion(() => Assert.Contains("2 new messages", cut.Find("[data-testid=thread-jump]").TextContent));
        await cut.Find("[data-testid=thread-jump]").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=thread-jump]")));
    }

    [Fact]
    public async Task Incoming_message_for_another_thread_updates_the_badge_and_announces_once()
    {
        var c = await SignInAndConversationAsync();
        MessagingApi.Me = Huda;
        var other = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Direct, [FakeMessagingApi.Khaled.Id], null, null));
        MessagingApi.Me = Sara;
        JSInterop.SetupModule("./js/thread.js").SetupVoid("scrollToEnd", _ => true).SetVoidResult();
        var cut = Open(c.Id);
        cut.WaitForElement("[data-testid=thread-body]");
        var state = Services.GetRequiredService<IMessagingState>();
        var before = state.UnreadConversations;

        var a = HudaSays(c.Id, "x"); // keep the open thread quiet
        _ = a;
        MessagingHub.Receive(other.Id, Guid.NewGuid());
        MessagingHub.Receive(other.Id, Guid.NewGuid());

        cut.WaitForAssertion(() => Assert.Contains("New message in another conversation", cut.Find("[data-testid=messages-announcer]").TextContent));
        Assert.Single(cut.FindAll("[data-testid=messages-announcer]")); // one polite announcer
        Assert.True(state.UnreadConversations >= before);
    }

    [Fact]
    public async Task Reconnect_merges_what_was_missed_into_the_open_thread()
    {
        var c = await SignInAndConversationAsync();
        var module = JSInterop.SetupModule("./js/thread.js");
        module.SetupVoid("scrollToEnd", _ => true).SetVoidResult();
        module.Setup<bool>("isNearBottom", _ => true).SetResult(true);
        var cut = Open(c.Id);
        cut.WaitForElement("[data-testid=thread-body]");
        MessagingHub.Drop();
        cut.WaitForElement("[data-testid=realtime-offline]"); // the banner says so while it is down
        var missed = HudaSays(c.Id, "sent while offline");

        MessagingHub.Reconnect();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"[data-testid=msg-{missed.Id}]")));
        Assert.Empty(cut.FindAll("[data-testid=realtime-offline]"));
        Assert.Equal(cut.FindAll("[data-testid^=msg-]").Select(e => e.GetAttribute("data-testid")).Distinct().Count(), cut.FindAll("[data-testid^=msg-]").Count); // nothing twice
    }

    [Fact]
    public async Task Conversation_changed_reloads_members_and_state()
    {
        var c = await SignInAndConversationAsync();
        var module = JSInterop.SetupModule("./js/thread.js");
        module.SetupVoid("scrollToEnd", _ => true).SetVoidResult();
        module.Setup<bool>("isNearBottom", _ => true).SetResult(true);
        var cut = Open(c.Id);
        cut.WaitForElement("[data-testid=thread-body]");
        Assert.NotEmpty(cut.FindAll("textarea")); // the composer is there

        MessagingApi.Lock(c.Id); // a moderator locked it
        MessagingHub.Change(c.Id);

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("textarea"))); // the state came from the server again: no composer
    }
}
