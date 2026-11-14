using Lms.Infrastructure.Comms.Preferences;
using Lms.Shared;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;

namespace Lms.UnitTests.Comms;

/// <summary>M6 Task 9 (pure part): the one effective-preference function, the change check and the comms-settings validation.</summary>
public sealed class EffectivePreferencesTests
{
    private const string InApp = NotificationChannels.InApp;
    private const string Email = NotificationChannels.Email;
    private const string Push = NotificationChannels.Push;

    private static NotificationEventDefinition Event(string[]? channels, bool mandatory = false) =>
        new("Test.Event", [], [], DefaultChannels: channels, Mandatory: mandatory);

    private static EffectivePreferenceDto Resolve(NotificationEventDefinition e, string channel, bool? choice = null, bool client = true, bool contact = true) =>
        EffectivePreferences.Resolve(new PreferenceInputs(e, channel, choice, client, contact));

    [Fact]
    public void In_app_is_always_on_whatever_the_user_chose_or_the_client_switched()
    {
        var e = Event(null);

        Assert.True(Resolve(e, InApp, choice: false, client: false, contact: false).Enabled);
        Assert.True(Resolve(Event(null, mandatory: true), InApp).Mandatory);
        Assert.Equal(PreferenceSources.Mandatory, Resolve(Event(null, mandatory: true), InApp).Source);
    }

    [Theory]
    // channel offered by the event, user choice, expected enabled, expected source
    [InlineData(true, null, true, "Default")]
    [InlineData(true, true, true, "User")]
    [InlineData(true, false, false, "User")]
    [InlineData(false, null, false, "Default")] // a user can only narrow: the event does not offer the channel
    [InlineData(false, true, false, "Default")]
    public void Effective_preference_table_for_a_normal_event(bool offered, bool? choice, bool enabled, string source)
    {
        var e = Event(offered ? [InApp, Email] : [InApp]);

        var cell = Resolve(e, Email, choice);

        Assert.Equal((enabled, source, false, (string?)null), (cell.Enabled, cell.Source, cell.Mandatory, cell.DisabledReason));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void A_mandatory_event_is_on_on_its_channels_whatever_the_user_chose(bool? choice)
    {
        var e = Event([InApp, Email, Push], mandatory: true);

        foreach (var channel in new[] { Email, Push })
        {
            var cell = Resolve(e, channel, choice);
            Assert.True(cell.Enabled);
            Assert.True(cell.Mandatory);
            Assert.Equal(PreferenceSources.Mandatory, cell.Source);
        }
    }

    [Fact]
    public void The_client_switch_and_a_missing_contact_win_and_say_why()
    {
        var e = Event([InApp, Email, Push], mandatory: true);

        var off = Resolve(e, Email, client: false);
        var noContact = Resolve(e, Push, contact: false);

        Assert.False(off.Enabled);
        Assert.Equal(DeliverySkipReasons.ChannelOff, off.DisabledReason);
        Assert.False(noContact.Enabled);
        Assert.Equal(DeliverySkipReasons.NoContact, noContact.DisabledReason);
        Assert.Equal(DeliverySkipReasons.ChannelOff, Resolve(Event([InApp, Email]), Email, client: false, contact: false).DisabledReason); // channel switch first
    }

    [Fact]
    public void No_cell_ever_offers_a_channel_outside_the_catalog()
    {
        foreach (var e in NotificationEvents.All)
        {
            foreach (var channel in NotificationChannels.All)
            {
                var cell = Resolve(e, channel);
                Assert.Contains(cell.Channel, NotificationChannels.All);
                Assert.DoesNotContain("sms", cell.Channel, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Real_mandatory_events_cannot_be_switched_off_and_the_absence_notice_reaches_email_and_push()
    {
        var absent = NotificationEvents.Find(NotificationEvents.AttendanceAbsent)!;

        Assert.Equal(ProblemCodes.notification_mandatory, EffectivePreferences.CheckChange(absent, Email, enabled: false, clientChannelEnabled: true));
        Assert.Equal(ProblemCodes.notification_mandatory, EffectivePreferences.CheckChange(absent, Push, enabled: false, clientChannelEnabled: true));
        Assert.True(Resolve(absent, Email, choice: false).Enabled);
        Assert.True(Resolve(absent, Push, choice: false).Enabled);
    }

    [Fact]
    public void Change_check_follows_the_problem_codes_of_the_contract()
    {
        var normal = Event([InApp, Email, Push]);

        Assert.Null(EffectivePreferences.CheckChange(normal, Email, enabled: false, clientChannelEnabled: true));
        Assert.Null(EffectivePreferences.CheckChange(normal, Email, enabled: true, clientChannelEnabled: true));
        Assert.Equal(ProblemCodes.in_app_required, EffectivePreferences.CheckChange(normal, InApp, enabled: false, clientChannelEnabled: true));
        Assert.Equal(ProblemCodes.channel_not_allowed, EffectivePreferences.CheckChange(normal, Push, enabled: true, clientChannelEnabled: false));
        Assert.Null(EffectivePreferences.CheckChange(normal, Push, enabled: false, clientChannelEnabled: false)); // narrowing is always fine
        Assert.Equal(ProblemCodes.validation_failed, EffectivePreferences.CheckChange(normal, "Sms", enabled: true, clientChannelEnabled: true));
        Assert.Equal(ProblemCodes.validation_failed, EffectivePreferences.CheckChange(null, Email, enabled: true, clientChannelEnabled: true));
        // a mandatory event only protects the channels it offers
        Assert.Null(EffectivePreferences.CheckChange(Event([InApp], mandatory: true), Email, enabled: false, clientChannelEnabled: true));
    }

    private static CommsSettingsDto Settings(int emailCap = 5000, int pushCap = 20000, int emailUser = 20, int pushUser = 50, string start = "21:00", string end = "07:00", int retention = 90) =>
        new(true, true, emailCap, pushCap, emailUser, pushUser, false, start, end, retention, false, 20, 10, 50, 10);

    [Fact]
    public void Default_settings_are_valid()
    {
        Assert.Empty(CommsSettingsRules.Validate(Settings()));
    }

    [Fact]
    public void Caps_above_the_platform_ceiling_are_refused_with_their_own_code()
    {
        var ceilings = CommsSettingsRules.Ceilings;

        var ok = CommsSettingsRules.Validate(Settings(emailCap: ceilings.MaxEmailPerDay, pushCap: ceilings.MaxPushPerDay));
        var over = CommsSettingsRules.Validate(Settings(emailCap: ceilings.MaxEmailPerDay + 1, pushCap: ceilings.MaxPushPerDay + 1));

        Assert.Empty(ok);
        Assert.Equal(
            [("MaxEmailPerDay", ProblemCodes.comms_cap_above_ceiling), ("MaxPushPerDay", ProblemCodes.comms_cap_above_ceiling)],
            over.Select(v => (v.Field, v.Code)));
        Assert.Equal((100_000, 200_000), (ceilings.MaxEmailPerDay, ceilings.MaxPushPerDay));
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("9:00")]
    [InlineData("21-00")]
    [InlineData("")]
    public void Malformed_quiet_hours_are_validation_errors(string time)
    {
        var violations = CommsSettingsRules.Validate(Settings(start: time));

        Assert.Equal([("QuietHoursStart", ProblemCodes.validation_failed)], violations.Select(v => (v.Field, v.Code)));
    }

    [Fact]
    public void Per_recipient_caps_and_retention_stay_inside_their_catalog_range()
    {
        Assert.Equal(["MaxEmailPerUserPerDay", "MaxPushPerUserPerDay", "DeliveryRetentionDays"],
            CommsSettingsRules.Validate(Settings(emailUser: 0, pushUser: 501, retention: 1)).Select(v => v.Field));
        Assert.Empty(CommsSettingsRules.Validate(Settings(emailUser: 1, pushUser: 500, retention: 730)));
    }
}
