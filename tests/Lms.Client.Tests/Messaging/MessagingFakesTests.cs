using Lms.Client.Services.Api;
using Lms.Client.Services.Messaging;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;

namespace Lms.Client.Tests.Messaging;

/// <summary>The M6 fakes mirror the server rules of #613 section 9 (wave-1 contracts): participants-only reading, the rules matrix, the delete window, blocks.</summary>
public sealed class MessagingFakesTests
{
    private static readonly DateTime T0 = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);

    private readonly FakeMessagingRules _rules = new();
    private readonly FakeMessagingApi _api;
    private DateTime _now = T0;

    public MessagingFakesTests()
    {
        _api = new FakeMessagingApi(_rules) { Now = () => _now };
    }

    private static CreateConversationRequest Direct(Guid to, string? first = null) => new(ConversationKinds.Direct, [to], null, first);

    private static async Task<ApiProblemException> Refused(Func<Task> call) => await Assert.ThrowsAsync<ApiProblemException>(call);

    [Fact]
    public async Task A_non_participant_gets_404_even_an_org_admin()
    {
        var conversation = await _api.CreateConversationAsync(Direct(FakeMessagingApi.Huda.Id, "hello"));

        _api.Me = FakeMessagingApi.Admin;

        Assert.Equal(404, (await Refused(() => _api.GetConversationAsync(conversation.Id))).Status);
        Assert.Equal(404, (await Refused(() => _api.GetMessagesAsync(conversation.Id, new MessageQuery()))).Status);
        Assert.Empty((await _api.ListConversationsAsync()).Items); // and the inbox never lists it
    }

    [Fact]
    public async Task The_rules_matrix_decides_who_can_start_a_conversation()
    {
        // Student to Student is Deny by default; Student to Teacher is Relationship (Huda teaches Sara, Layla does not)
        Assert.Equal(403, (await Refused(() => _api.CreateConversationAsync(Direct(FakeMessagingApi.Omar.Id)))).Status);
        Assert.Equal(ProblemCodes.messaging_not_allowed, (await Refused(() => _api.CreateConversationAsync(Direct(FakeMessagingApi.Layla.Id)))).Code);
        var ok = await _api.CreateConversationAsync(Direct(FakeMessagingApi.Huda.Id));

        Assert.Equal(ConversationKinds.Direct, ok.Kind);
    }

    [Fact]
    public async Task The_same_pair_gets_the_existing_direct_conversation()
    {
        var first = await _api.CreateConversationAsync(Direct(FakeMessagingApi.Huda.Id));
        var again = await _api.CreateConversationAsync(Direct(FakeMessagingApi.Huda.Id));

        Assert.Equal(first.Id, again.Id);
    }

    [Fact]
    public async Task Recipients_are_only_the_people_the_role_may_message()
    {
        var recipients = await _api.GetRecipientsAsync();

        Assert.Equal([FakeMessagingApi.Huda.Id], recipients.Select(r => r.UserId)); // a student reaches only the related teacher
        var summary = await _api.GetRulesSummaryAsync();
        Assert.Contains(BuiltInRoles.Teacher, summary.ReachableRoleKeys);
        Assert.DoesNotContain(BuiltInRoles.Student, summary.ReachableRoleKeys);
    }

    [Fact]
    public async Task A_message_is_deleted_by_its_sender_inside_the_window_only()
    {
        var conversation = await _api.CreateConversationAsync(Direct(FakeMessagingApi.Huda.Id));
        var mine = await _api.SendAsync(conversation.Id, new SendMessageRequest("a typo", null));
        Assert.True(mine.CanDelete);

        _now = T0.AddMinutes(MessagingLimits.DeleteWindowMinutes + 1);

        Assert.Equal(ProblemCodes.message_delete_window_closed, (await Refused(() => _api.DeleteMessageAsync(conversation.Id, mine.Id))).Code);
        _now = T0.AddMinutes(1);
        await _api.DeleteMessageAsync(conversation.Id, mine.Id);
        var read = (await _api.GetMessagesAsync(conversation.Id, new MessageQuery())).Items.Single(m => m.Id == mine.Id);
        Assert.Equal(MessageStates.Deleted, read.State);
        Assert.Null(read.Body); // for everyone: the body is gone
    }

    [Fact]
    public async Task Nobody_deletes_someone_elses_message()
    {
        var conversation = await _api.CreateConversationAsync(Direct(FakeMessagingApi.Huda.Id));
        var theirs = _api.Receive(conversation.Id, FakeMessagingApi.Huda, "hello");

        Assert.Equal(403, (await Refused(() => _api.DeleteMessageAsync(conversation.Id, theirs.Id))).Status);
    }

    [Fact]
    public async Task A_locked_conversation_refuses_sending_and_a_long_body_is_refused()
    {
        var conversation = await _api.CreateConversationAsync(Direct(FakeMessagingApi.Huda.Id));
        Assert.Equal(ProblemCodes.message_too_long, (await Refused(() => _api.SendAsync(conversation.Id, new SendMessageRequest(new string('x', MessagingLimits.MaxBodyLength + 1), null)))).Code);

        _api.Lock(conversation.Id);

        Assert.Equal(ProblemCodes.conversation_closed, (await Refused(() => _api.SendAsync(conversation.Id, new SendMessageRequest("hi", null)))).Code);
    }

    [Fact]
    public async Task Unread_counts_follow_marking_read_and_the_badge_counts_conversations()
    {
        var conversation = await _api.CreateConversationAsync(Direct(FakeMessagingApi.Huda.Id));
        _api.Receive(conversation.Id, FakeMessagingApi.Huda, "one");
        _api.Receive(conversation.Id, FakeMessagingApi.Huda, "two");

        Assert.Equal(2, (await _api.ListConversationsAsync()).Items.Single().UnreadCount);
        Assert.Equal(1, (await _api.GetUnreadTotalAsync()).Conversations);

        await _api.MarkReadAsync(conversation.Id);

        Assert.Equal(0, (await _api.GetUnreadTotalAsync()).Conversations);
    }

    [Fact]
    public async Task A_block_silently_stops_direct_messages_and_search_sees_only_own_conversations()
    {
        var conversation = await _api.CreateConversationAsync(Direct(FakeMessagingApi.Huda.Id));
        _api.Me = FakeMessagingApi.Huda;
        await _api.BlockAsync(FakeMessagingApi.Sara.Id);
        _api.Me = FakeMessagingApi.Sara;

        var sent = await _api.SendAsync(conversation.Id, new SendMessageRequest("are you there", null)); // the blocked person is never told

        Assert.NotNull(sent);
        _api.Me = FakeMessagingApi.Huda;
        Assert.Empty((await _api.GetMessagesAsync(conversation.Id, new MessageQuery())).Items);
        Assert.Empty(await _api.SearchAsync("are you"));
        _api.Me = FakeMessagingApi.Sara;
        Assert.Single(await _api.SearchAsync("are you"));
    }

    [Fact]
    public async Task Search_needs_two_characters_and_a_report_is_unique_and_not_on_own_message()
    {
        var conversation = await _api.CreateConversationAsync(Direct(FakeMessagingApi.Huda.Id));
        var mine = await _api.SendAsync(conversation.Id, new SendMessageRequest("mine", null));
        var theirs = _api.Receive(conversation.Id, FakeMessagingApi.Huda, "theirs");

        Assert.Equal(400, (await Refused(() => _api.SearchAsync("a"))).Status);
        Assert.Equal(404, (await Refused(() => _api.ReportAsync(mine.Id, new ReportMessageRequest(ReportReasons.Spam, null)))).Status);
        await _api.ReportAsync(theirs.Id, new ReportMessageRequest(ReportReasons.Spam, null));
        Assert.Equal(ProblemCodes.report_duplicate, (await Refused(() => _api.ReportAsync(theirs.Id, new ReportMessageRequest(ReportReasons.Spam, null)))).Code);
    }

    [Fact]
    public async Task Messages_page_backwards_with_the_next_before_id()
    {
        var conversation = await _api.CreateConversationAsync(Direct(FakeMessagingApi.Huda.Id));
        for (var i = 0; i < 5; i++)
        {
            _now = T0.AddMinutes(i);
            await _api.SendAsync(conversation.Id, new SendMessageRequest("m" + i, null));
        }

        var first = await _api.GetMessagesAsync(conversation.Id, new MessageQuery(null, 2));
        Assert.Equal(["m4", "m3"], first.Items.Select(m => m.Body));
        var second = await _api.GetMessagesAsync(conversation.Id, new MessageQuery(first.NextBeforeId, 2));
        Assert.Equal(["m2", "m1"], second.Items.Select(m => m.Body));
    }
}
