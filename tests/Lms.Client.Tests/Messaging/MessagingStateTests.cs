using Bunit;
using Lms.Client.Services.Messaging;
using Lms.Client.Tests.Support;
using Lms.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Messaging;

public sealed class MessagingStateTests : ClientTestContext
{
    private readonly FakeMessagingApi _api = new();

    public MessagingStateTests()
    {
        Services.AddSingleton<IMessagingApi>(_api);
        Services.AddScoped<IMessagingState, MessagingState>();
    }

    private IMessagingState State => Services.GetRequiredService<IMessagingState>();

    private async Task<Guid> SendToSaraAsync()
    {
        _api.Me = FakeMessagingApi.Huda;
        var c = await _api.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Direct, [FakeMessagingApi.Sara.Id], null, "hi"));
        _api.Me = FakeMessagingApi.Sara;
        return c.Id;
    }

    [Fact]
    public async Task Unread_total_counts_conversations_not_messages()
    {
        await Context.SetAsync(Guid.NewGuid());
        await SendToSaraAsync();
        await State.RefreshAsync();
        Assert.Equal(1, State.UnreadConversations);
    }

    [Fact]
    public async Task Unread_total_updates_on_read()
    {
        await Context.SetAsync(Guid.NewGuid());
        var id = await SendToSaraAsync();
        await State.RefreshAsync();
        var changes = 0;
        State.Changed += () => changes++;

        await _api.MarkReadAsync(id);
        await State.RefreshAsync();

        Assert.Equal(0, State.UnreadConversations);
        Assert.True(changes > 0);
    }

    [Fact]
    public async Task Unread_total_updates_on_a_hub_event()
    {
        await Context.SetAsync(Guid.NewGuid());
        await State.RefreshAsync();
        var changes = 0;
        State.Changed += () => changes++;

        State.Apply(3);

        Assert.Equal(3, State.UnreadConversations);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Without_a_context_there_is_nothing_to_count()
    {
        await State.RefreshAsync();
        Assert.Equal(0, State.UnreadConversations);
    }

    [Fact]
    public async Task Send_updates_the_badge_when_the_context_changes_account()
    {
        await Context.SetAsync(Guid.NewGuid());
        await SendToSaraAsync();
        await State.RefreshAsync();
        await Context.ClearAsync();
        await State.RefreshAsync();
        Assert.Equal(0, State.UnreadConversations);
    }
}
