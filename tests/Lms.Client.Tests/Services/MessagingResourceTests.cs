using System.Globalization;
using Lms.Client.Resources;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Tests.Services;

/// <summary>M6 wave 1: every messaging code, event, setting and catalog value has a label in both languages; no notice shows message text.</summary>
public sealed class MessagingResourceTests : ClientTestContext
{
    private static readonly string[] Codes =
    [
        ProblemCodes.messaging_not_allowed, ProblemCodes.conversation_closed, ProblemCodes.message_too_long, ProblemCodes.message_delete_window_closed,
        ProblemCodes.report_duplicate, ProblemCodes.rate_limited, ProblemCodes.notification_mandatory, ProblemCodes.announcement_target_invalid,
        ProblemCodes.announcement_not_editable, ProblemCodes.rules_looser_than_default,
        ProblemCodes.conversation_read_only, ProblemCodes.comms_cap_above_ceiling, ProblemCodes.attachment_invalid,
    ];

    private static IEnumerable<string> Keys()
    {
        foreach (var code in Codes)
        {
            yield return "Error_" + code;
        }

        foreach (var d in SettingCatalog.All.Where(d => d.Name.StartsWith("Comms.") || d.Name.StartsWith("Messaging.") || d.Name.StartsWith("Moderation.") || d.Name.StartsWith("Announcements.")))
        {
            yield return "Setting_" + d.Name.Replace('.', '_');
        }

        foreach (var key in new[]
                 {
                     NotificationEvents.MessagesNewMessage, NotificationEvents.AnnouncementsPublished, NotificationEvents.MessagingWarning,
                     NotificationEvents.MessagingRestricted, NotificationEvents.MessagingReportResolved, NotificationEvents.CommsCapReached,
                     NotificationEvents.ModerationReportReceived, NotificationEvents.ModerationSafetyReportReceived,
                 })
        {
            yield return "NotifEvent_" + key.Replace('.', '_');
            yield return "Notif_" + key.Replace('.', '_');
        }

        foreach (var (prefix, values) in new (string, IReadOnlyList<string>)[]
                 {
                     ("Msg_Kind_", ConversationKinds.All), ("Msg_State_", MessageStates.All), ("Msg_Rule_", MessagingRuleModes.All),
                     ("Msg_Reason_", ReportReasons.All), ("Msg_ReportStatus_", ReportStatuses.All), ("Msg_Action_", ModerationActions.All), ("Msg_MyReport_", MyReportStatuses.All),
                     ("Ann_TargetKind_", AnnouncementTargetKinds.All), ("Ann_Status_", AnnouncementStatuses.All), ("Ann_Source_", AnnouncementSources.All), ("Ann_Role_", AnnouncementRoleFilters.All),
                     ("Msg_ReadOnly_", ConversationReadOnlyReasons.All), ("Msg_Denied_", MessagingNotAllowedReasons.All), ("Comms_Delivery_", DeliveryStatuses.All), ("Comms_ChannelState_", ChannelStates.All), ("Comms_PrefSource_", PreferenceSources.All),
                 })
        {
            foreach (var v in values)
            {
                yield return prefix + v;
            }
        }

        foreach (var p in new[] { "Messaging_View", "Messaging_Send", "Messaging_Moderate", "Announcements_View", "Announcements_Manage", "Comms_Manage" })
        {
            yield return "Perm_" + p;
        }

        foreach (var days in RestrictionDurations.Days)
        {
            yield return "Msg_RestrictDays_" + days;
        }

        foreach (var e in new[] { "Conversation", "MessageReport", "MessagingRestriction", "MessagingRule", "Announcement", "ModerationRead" })
        {
            yield return "AuditEntity_" + e;
        }
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Every_messaging_label_exists(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var l = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        Assert.Empty(Keys().Where(k => l[k].ResourceNotFound).ToList());
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Notice_texts_never_carry_message_text_or_a_body(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var l = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        foreach (var key in new[] { "Notif_Messages_NewMessage", "Notif_Announcements_Published", "Notif_Messaging_Warning" })
        {
            var text = l[key].Value;
            Assert.DoesNotContain("{Body}", text);
            Assert.DoesNotContain("{Message}", text);
            Assert.DoesNotContain("{Text}", text);
        }

        Assert.Contains("{SenderName}", l["Notif_Messages_NewMessage"].Value);
        Assert.Contains("{Title}", l["Notif_Announcements_Published"].Value);
    }
}
