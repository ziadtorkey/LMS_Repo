using Lms.Client.Services.Api;
using Lms.Client.Services.Messaging;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;

namespace Lms.Client.Tests.Messaging;

/// <summary>The announcements and preferences fakes mirror the server rules: narrow-only rules (D69), mandatory events, drafts only editable, three test emails a day.</summary>
public sealed class CommsFakesTests
{
    private static readonly DateTime T0 = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly AnnouncementTargetDto[] Unit = [new(AnnouncementTargetKinds.Unit, FakeMessagingApi.UnitA, true)];

    private readonly FakeAnnouncementsApi _ann = new() { Now = () => T0 };
    private readonly FakeMessagingRules _rules = new();
    private readonly FakeCommsApi _comms;

    public CommsFakesTests() => _comms = new FakeCommsApi(_rules) { Now = () => T0 };

    private static CreateAnnouncementRequest New(bool publish, DateTime? at = null, bool pin = false, Guid? unit = null) =>
        new(unit ?? FakeMessagingApi.UnitA, "عنوان", "Title", "نص", "Body", Unit, null, at, null, pin, true, null, publish);

    private static async Task<ApiProblemException> Refused(Func<Task> call) => await Assert.ThrowsAsync<ApiProblemException>(call);

    [Fact]
    public async Task Rules_can_only_be_narrowed()
    {
        // Teacher to Student defaults to Relationship: Deny is narrower and is saved, Allow is looser and is refused
        await _comms.SaveMessagingRulesAsync(new SaveMessagingRulesRequest([new MessagingRuleCellDto(BuiltInRoles.Teacher, BuiltInRoles.Student, MessagingRuleModes.Deny)]));
        Assert.Equal(MessagingRuleModes.Deny, _rules.Mode(BuiltInRoles.Teacher, BuiltInRoles.Student));

        var refused = await Refused(() => _comms.SaveMessagingRulesAsync(new SaveMessagingRulesRequest([new MessagingRuleCellDto(BuiltInRoles.Student, BuiltInRoles.Student, MessagingRuleModes.Allow)])));

        Assert.Equal(ProblemCodes.rules_looser_than_default, refused.Code);
        Assert.Equal(MessagingRuleModes.Deny, _rules.Mode(BuiltInRoles.Student, BuiltInRoles.Student));
    }

    [Fact]
    public async Task A_narrowed_rule_changes_who_a_user_can_message()
    {
        var messaging = new FakeMessagingApi(_rules);
        Assert.Single(await messaging.GetRecipientsAsync());

        await _comms.SaveMessagingRulesAsync(new SaveMessagingRulesRequest([new MessagingRuleCellDto(BuiltInRoles.Student, BuiltInRoles.Teacher, MessagingRuleModes.Deny)]));

        Assert.Empty(await messaging.GetRecipientsAsync());
    }

    [Fact]
    public async Task A_mandatory_event_cannot_be_switched_off_on_its_mandatory_channel()
    {
        var refused = await Refused(() => _comms.SavePreferencesAsync(new SavePreferencesRequest([new SavePreferenceRequest(NotificationEvents.MessagingWarning, NotificationChannels.InApp, false)])));

        Assert.Equal(ProblemCodes.notification_mandatory, refused.Code);
        var row = (await _comms.GetEffectivePreferencesAsync()).Single(p => p.EventKey == NotificationEvents.MessagingWarning && p.Channel == NotificationChannels.InApp);
        Assert.True(row.Enabled);
        Assert.True(row.Mandatory);
    }

    [Fact]
    public async Task A_user_preference_overrides_the_default_and_names_its_source()
    {
        var key = NotificationEvents.All.First(e => !e.Mandatory).Key;

        await _comms.SavePreferencesAsync(new SavePreferencesRequest([new SavePreferenceRequest(key, NotificationChannels.Email, true)]));

        var row = (await _comms.GetEffectivePreferencesAsync()).Single(p => p.EventKey == key && p.Channel == NotificationChannels.Email);
        Assert.Equal((true, PreferenceSources.User, false), (row.Enabled, row.Source, row.Mandatory));
    }

    [Fact]
    public async Task The_test_email_is_limited_to_three_a_day()
    {
        for (var i = 0; i < MessagingLimits.EmailTestsPerDay; i++) Assert.True((await _comms.SendTestEmailAsync()).Accepted);

        Assert.Equal(ProblemCodes.rate_limited, (await Refused(() => _comms.SendTestEmailAsync())).Code);
    }

    [Fact]
    public async Task Push_subscriptions_are_removed_by_their_owner_only()
    {
        var sub = await _comms.AddPushSubscriptionAsync(new SavePushSubscriptionRequest("https://push.example/abc", "k", "a", "Phone"));
        Assert.Single(await _comms.GetPushSubscriptionsAsync());

        await _comms.RemovePushSubscriptionAsync(sub.Id);

        Assert.Empty(await _comms.GetPushSubscriptionsAsync());
        Assert.Equal(404, (await Refused(() => _comms.RemovePushSubscriptionAsync(sub.Id))).Status);
    }

    [Fact]
    public async Task A_draft_is_edited_published_and_withdrawn_and_only_a_draft_is_deleted()
    {
        var draft = await _ann.CreateAsync(New(publish: false));
        Assert.Equal(AnnouncementStatuses.Draft, draft.Status);
        Assert.Null(draft.AudienceCount);

        var published = await _ann.PublishAsync(draft.Id);
        Assert.Equal(AnnouncementStatuses.Published, published.Status);
        Assert.True(published.AudienceCount > 0);
        Assert.Equal(ProblemCodes.announcement_not_editable, (await Refused(() => _ann.DeleteAsync(draft.Id))).Code);
        Assert.Equal(ProblemCodes.announcement_not_editable,
            (await Refused(() => _ann.UpdateAsync(draft.Id, new UpdateAnnouncementRequest("ع", "T", null, null, Unit, null, null, null, false, false, null)))).Code);

        Assert.Equal(AnnouncementStatuses.Withdrawn, (await _ann.WithdrawAsync(draft.Id)).Status);
        Assert.Empty((await _ann.ListMineAsync()).Items); // recipients no longer see it
    }

    [Fact]
    public async Task A_publish_time_in_the_future_is_scheduled_and_not_yet_visible_to_readers()
    {
        var scheduled = await _ann.CreateAsync(New(publish: true, at: T0.AddDays(1)));

        Assert.Equal(AnnouncementStatuses.Scheduled, scheduled.Status);
        Assert.Empty((await _ann.ListMineAsync()).Items);
    }

    [Fact]
    public async Task Readers_see_pinned_first_and_mark_only_published_items_read()
    {
        await _ann.CreateAsync(New(publish: true));
        var pinned = await _ann.CreateAsync(New(publish: true, pin: true));
        var draft = await _ann.CreateAsync(New(publish: false));

        var mine = (await _ann.ListMineAsync()).Items;
        Assert.Equal(pinned.Id, mine[0].Id);
        await _ann.MarkReadAsync(pinned.Id);
        Assert.True((await _ann.ListMineAsync()).Items.First(a => a.Id == pinned.Id).IsRead);
        Assert.Equal(404, (await Refused(() => _ann.MarkReadAsync(draft.Id))).Status); // not their recipient row
    }

    [Fact]
    public async Task Targets_are_required_the_unit_is_checked_and_the_preview_counts_the_audience()
    {
        Assert.Equal(ProblemCodes.announcement_target_invalid,
            (await Refused(() => _ann.CreateAsync(New(publish: false) with { Targets = [] }))).Code);
        Assert.Equal(403, (await Refused(() => _ann.CreateAsync(New(publish: false, unit: Guid.NewGuid())))).Status);

        var all = await _ann.PreviewAudienceAsync(new AudiencePreviewRequest(FakeMessagingApi.UnitA, Unit, null));
        var guardians = await _ann.PreviewAudienceAsync(new AudiencePreviewRequest(FakeMessagingApi.UnitA, Unit, [AnnouncementRoleFilters.Guardians]));

        Assert.True(all.Count > guardians.Count);
        Assert.Equal(_ann.RoleCounts[AnnouncementRoleFilters.Guardians], guardians.Count);
    }
}
