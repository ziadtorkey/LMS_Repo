using Lms.Client.Services.Api;
using Lms.Client.Services.Messaging;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;

namespace Lms.Client.Tests.Messaging;

/// <summary>Mute and leave, D69 (only staff create groups), D74 and D77 (students read groups, guardians may post in teacher-run groups), D76 (no SMS), sender-only delete.</summary>
public sealed class MessagingGroupRulesTests
{
    private static readonly DateTime T0 = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
    private readonly FakeMessagingRules _rules = new();
    private readonly FakeMessagingApi _api;
    private readonly FakeCommsApi _comms;

    public MessagingGroupRulesTests()
    {
        _api = new FakeMessagingApi(_rules) { Now = () => T0 };
        _comms = new FakeCommsApi(_rules) { Now = () => T0 };
    }

    private static async Task<ApiProblemException> Refused(Func<Task> call) => await Assert.ThrowsAsync<ApiProblemException>(call);

    /// <summary>A teacher-run group of the teacher, a student and a guardian.</summary>
    private async Task<ConversationDto> TeacherGroupAsync()
    {
        _api.Me = FakeMessagingApi.Huda;
        _api.Related.Add((FakeMessagingApi.Huda.Id, FakeMessagingApi.Omar.Id));
        return await _api.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Group,
            [FakeMessagingApi.Sara.Id, FakeMessagingApi.Khaled.Id], "Class 10-A", "Welcome", Guid.NewGuid()));
    }

    [Fact]
    public async Task A_student_and_a_guardian_cannot_create_a_group()
    {
        _api.Me = FakeMessagingApi.Sara;
        Assert.False((await _api.GetRulesSummaryAsync()).CanCreateGroups); // the dialog never offers a group title to a student
        var student = await Refused(() => _api.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Group, [FakeMessagingApi.Huda.Id, FakeMessagingApi.Layla.Id], "x", "hi")));
        Assert.Equal(ProblemCodes.messaging_not_allowed, student.Code);

        _api.Me = FakeMessagingApi.Khaled;
        Assert.False((await _api.GetRulesSummaryAsync()).CanCreateGroups);
        Assert.Equal(403, (await Refused(() => _api.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Group, [FakeMessagingApi.Huda.Id, FakeMessagingApi.Layla.Id], "x", "hi")))).Status);

        _api.Me = FakeMessagingApi.Huda;
        Assert.True((await _api.GetRulesSummaryAsync()).CanCreateGroups);
    }

    [Fact]
    public async Task A_student_cannot_post_in_a_class_group_but_can_message_the_teacher()
    {
        var group = await TeacherGroupAsync();

        _api.Me = FakeMessagingApi.Sara;
        Assert.True((await _api.GetConversationAsync(group.Id)).IsReadOnly); // read-only for the student
        Assert.Equal(ProblemCodes.group_read_only, (await Refused(() => _api.SendAsync(group.Id, new SendMessageRequest("hi all", null)))).Code);
        Assert.Equal(403, (await Refused(() => _api.SendAsync(group.Id, new SendMessageRequest("hi all", null)))).Status);
        Assert.Contains(group.Id, (await _api.ListConversationsAsync()).Items.Where(i => i.IsReadOnly).Select(i => i.Id)); // and she still reads it

        var direct = await _api.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Direct, [FakeMessagingApi.Huda.Id], null, "a private question"));
        Assert.False(direct.IsReadOnly);

        _api.Me = FakeMessagingApi.Huda;
        Assert.False((await _api.GetConversationAsync(group.Id)).IsReadOnly);
        await _api.SendAsync(group.Id, new SendMessageRequest("teacher post", null)); // the teacher posts
    }

    [Fact]
    public async Task A_guardian_may_post_in_a_teacher_run_group_that_includes_guardians()
    {
        var group = await TeacherGroupAsync();

        _api.Me = FakeMessagingApi.Khaled;

        Assert.False((await _api.GetConversationAsync(group.Id)).IsReadOnly);
        Assert.NotNull(await _api.SendAsync(group.Id, new SendMessageRequest("thank you", null)));
    }

    [Fact]
    public async Task Mute_hides_nothing_and_still_counts_unread_and_a_past_date_is_refused()
    {
        var group = await TeacherGroupAsync();
        _api.Me = FakeMessagingApi.Khaled;

        var muted = await _api.MuteAsync(group.Id, new MuteConversationRequest(null));
        Assert.True(muted.Muted);
        Assert.Equal(400, (await Refused(() => _api.MuteAsync(group.Id, new MuteConversationRequest(T0.AddMinutes(-1))))).Status);

        var item = (await _api.ListConversationsAsync()).Items.Single();
        Assert.True(item.Muted);
        Assert.Equal(1, item.UnreadCount); // the unread count still counts
        await _api.UnmuteAsync(group.Id);
        Assert.False((await _api.GetConversationAsync(group.Id)).Muted);
    }

    [Fact]
    public async Task Leaving_a_group_keeps_the_history_for_the_others_and_a_direct_conversation_cannot_be_left()
    {
        var group = await TeacherGroupAsync();
        _api.Me = FakeMessagingApi.Khaled;

        await _api.LeaveAsync(group.Id);

        Assert.Equal(404, (await Refused(() => _api.GetConversationAsync(group.Id))).Status); // a left participant gets 404
        Assert.Empty((await _api.ListConversationsAsync()).Items);
        _api.Me = FakeMessagingApi.Huda;
        var seen = await _api.GetConversationAsync(group.Id);
        Assert.True(seen.Participants.Single(p => p.UserId == FakeMessagingApi.Khaled.Id).Left); // the others see "left"
        Assert.NotEmpty((await _api.GetMessagesAsync(group.Id, new MessageQuery())).Items);

        var direct = await _api.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Direct, [FakeMessagingApi.Sara.Id], null, "hi"));
        Assert.Equal(ProblemCodes.conversation_closed, (await Refused(() => _api.LeaveAsync(direct.Id))).Code);
    }

    [Fact]
    public async Task Only_the_owner_adds_and_removes_members_by_the_rules_and_a_group_never_drops_below_three()
    {
        var group = await TeacherGroupAsync();

        _api.Me = FakeMessagingApi.Sara;
        Assert.Equal(404, (await Refused(() => _api.AddParticipantsAsync(group.Id, new AddParticipantsRequest([FakeMessagingApi.Omar.Id])))).Status); // another participant

        _api.Me = FakeMessagingApi.Huda;
        _api.Related.Remove((FakeMessagingApi.Huda.Id, FakeMessagingApi.Omar.Id)); // owner to member: the teacher has no relationship with Omar
        Assert.Equal(ProblemCodes.messaging_not_allowed, (await Refused(() => _api.AddParticipantsAsync(group.Id, new AddParticipantsRequest([FakeMessagingApi.Omar.Id])))).Code);
        _api.Related.Add((FakeMessagingApi.Huda.Id, FakeMessagingApi.Omar.Id));
        var withOmar = await _api.AddParticipantsAsync(group.Id, new AddParticipantsRequest([FakeMessagingApi.Omar.Id]));
        Assert.Equal(4, withOmar.Participants.Count);

        await _api.RemoveParticipantAsync(group.Id, FakeMessagingApi.Omar.Id);
        Assert.Equal(400, (await Refused(() => _api.RemoveParticipantAsync(group.Id, FakeMessagingApi.Khaled.Id))).Status); // three would become two
    }

    [Fact]
    public async Task Another_persons_message_is_not_deletable_and_answers_404()
    {
        var group = await TeacherGroupAsync();
        var message = (await _api.GetMessagesAsync(group.Id, new MessageQuery())).Items.First();
        _api.Me = FakeMessagingApi.Khaled;

        Assert.Equal(404, (await Refused(() => _api.DeleteMessageAsync(group.Id, message.Id))).Status);
    }

    [Fact]
    public async Task There_is_no_sms_channel_preference_or_provider_and_saving_one_is_refused()
    {
        Assert.DoesNotContain(NotificationChannels.Sms, (await _comms.GetChannelsAsync()).Select(c => c.Channel));
        Assert.DoesNotContain(NotificationChannels.Sms, (await _comms.GetEffectivePreferencesAsync()).Select(p => p.Channel));
        Assert.DoesNotContain(NotificationChannels.Sms, (await _comms.GetProviderStatusAsync()).Select(p => p.Channel));

        var refused = await Refused(() => _comms.SavePreferencesAsync(new SavePreferencesRequest([new SavePreferenceRequest(NotificationEvents.All[0].Key, NotificationChannels.Sms, true)])));

        Assert.Equal(ProblemCodes.channel_not_allowed, refused.Code);
        Assert.Equal([NotificationChannels.InApp, NotificationChannels.Email, NotificationChannels.Push], FakeCommsApi.Offered);
    }
}
