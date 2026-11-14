using System.Text.Json;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Me;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;

namespace Lms.UnitTests.Contracts;

/// <summary>M6 wave 1 Lead amendments to #724 (plan Task 1): hub names, uploads, CanPost, capabilities, L18 codes, the matrix row by row.</summary>
public sealed class MessagingWave1AmendmentTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static T Round<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Web), Web)!;

    [Fact]
    public void Hub_method_names_are_the_three_id_only_events()
    {
        Assert.Equal("MessageReceived", NotificationHub.MessageReceivedMethod);
        Assert.Equal("ConversationRead", NotificationHub.ConversationReadMethod);
        Assert.Equal("ConversationChanged", NotificationHub.ConversationChangedMethod);
        Assert.Equal(["notification", "MessageReceived", "ConversationRead", "ConversationChanged"], NotificationHub.ServerToClientMethods);
    }

    [Fact]
    public void Hub_event_payloads_are_ids_only()
    {
        foreach (var type in new[] { typeof(MessageReceivedEvent), typeof(ConversationReadEvent), typeof(ConversationChangedEvent) })
        {
            Assert.All(type.GetProperties(), p => Assert.Equal(typeof(Guid), p.PropertyType));
        }

        var read = Round(new ConversationReadEvent(Guid.NewGuid(), Guid.NewGuid()));
        Assert.NotEqual(read.ConversationId, read.LastReadMessageId);
        Assert.Equal(["SwitchContext"], NotificationHub.ClientCallableMethods);
    }

    [Fact]
    public void Upload_routes_and_the_pending_attachment_are_in_the_contract()
    {
        var c = Guid.NewGuid();
        var a = Guid.NewGuid();
        var m = Guid.NewGuid();

        Assert.Equal("/api/conversations/{id:guid}/attachments", Routes.ConversationAttachmentsPattern);
        Assert.Equal("/api/conversations/{id:guid}/attachments/{aid:guid}", Routes.ConversationAttachmentPattern);
        Assert.Equal("/api/conversations/{id:guid}/messages/{mid:guid}/attachments/{fid:guid}/url", Routes.MessageAttachmentUrlPattern);
        Assert.Equal($"/api/conversations/{c}/attachments", Routes.ConversationAttachments(c));
        Assert.Equal($"/api/conversations/{c}/attachments/{a}", Routes.ConversationAttachment(c, a));
        Assert.Equal($"/api/conversations/{c}/messages/{m}/attachments/{a}/url", Routes.MessageAttachmentUrl(c, m, a));

        var pending = Round(new PendingAttachmentDto(a, "a.pdf", "application/pdf", 10, DateTime.UtcNow.AddHours(1)));
        Assert.Equal(a, pending.FileId);
        Assert.Equal(["Body", "AttachmentFileIds"], typeof(SendMessageRequest).GetProperties().Select(p => p.Name));
        Assert.Equal(10, SettingKeys.MessagingUploadsPerMinute.Default);
        Assert.Equal("Messaging.UploadsPerMinute", SettingKeys.MessagingUploadsPerMinute.Name);
        Assert.Contains(SettingCatalog.All, d => d.Name == "Messaging.UploadsPerMinute" && d.ClientLevelOnly);
    }

    [Fact]
    public void Conversation_dto_carries_can_post_and_the_read_only_reason()
    {
        var dto = new ConversationDto(Guid.NewGuid(), ConversationKinds.Group, "t", [], false, false, DateTime.UtcNow);
        Assert.True(dto.CanPost);
        Assert.Null(dto.ReadOnlyReason);

        var closed = Round(dto with { CanPost = false, ReadOnlyReason = ConversationReadOnlyReasons.GroupStudentsReadOnly });
        Assert.False(closed.CanPost);
        Assert.Equal("group_students_read_only", closed.ReadOnlyReason);
        Assert.Equal(
            ["group_students_read_only", "group_guardians_read_only", "rule", "blocked_or_rule", "restricted", "locked"],
            ConversationReadOnlyReasons.All);
    }

    [Fact]
    public void Messaging_not_allowed_reasons_are_the_l18_set()
    {
        Assert.Equal(["rule", "relationship", "restricted", "blocked_or_rule", "private_group_with_student", "group_full"], MessagingNotAllowedReasons.All);
    }

    [Fact]
    public void Every_l18_problem_code_exists_with_its_own_name()
    {
        string[] expected =
        [
            "rate_limited", "notification_mandatory", "messaging_not_allowed", "conversation_closed", "conversation_read_only", "message_too_long",
            "message_delete_window_closed", "rules_looser_than_default", "announcement_target_invalid", "announcement_not_editable",
            "comms_cap_above_ceiling", "report_duplicate", "attachment_invalid", "feature_disabled", "provider_not_configured", "concurrency_conflict",
        ];
        var fields = typeof(ProblemCodes).GetFields().ToDictionary(f => f.Name, f => (string?)f.GetRawConstantValue());

        Assert.All(expected, code => Assert.Equal(code, fields[code]));
    }

    [Fact]
    public void Capabilities_route_and_dto_report_the_effective_flags()
    {
        Assert.Equal("/api/me/capabilities", Routes.MeCapabilities);
        var dto = Round(new CapabilitiesDto(new Dictionary<string, bool> { [FeatureFlags.Messaging.Name] = true }));

        Assert.True(dto.Has(FeatureFlags.Messaging));
        Assert.False(dto.Has(FeatureFlags.GuardianPortal)); // a flag that is not listed is off
    }

    [Fact]
    public void Announcement_routes_live_under_comms_and_the_reader_feed_stays_under_me()
    {
        Assert.All(
            [Routes.OrgAnnouncements, Routes.OrgAnnouncementPattern, Routes.OrgAnnouncementPublishPattern, Routes.OrgAnnouncementWithdrawPattern,
             Routes.OrgAnnouncementAudiencePreview, Routes.OrgAnnouncementCapabilities],
            r => Assert.StartsWith("/api/comms/announcements", r, StringComparison.Ordinal));
        Assert.Equal("/api/me/announcements", Routes.MyAnnouncements);
    }

    [Fact]
    public void Default_matrix_matches_the_plan_table_row_by_row()
    {
        const string deny = MessagingRuleModes.Deny, rel = MessagingRuleModes.Relationship, same = MessagingRuleModes.SameUnit;
        string[] roles =
        [
            BuiltInRoles.Student, BuiltInRoles.Guardian, BuiltInRoles.Teacher, BuiltInRoles.Counselor,
            BuiltInRoles.Supervisor, BuiltInRoles.Manager, BuiltInRoles.OrgAdmin,
        ];
        // columns: Student, Guardian, Teacher, Counselor, Supervisor, Manager, OrgAdmin
        var table = new Dictionary<string, string[]>
        {
            [BuiltInRoles.Student] = [deny, deny, rel, rel, deny, deny, deny],
            [BuiltInRoles.Guardian] = [deny, deny, rel, rel, deny, deny, deny],
            [BuiltInRoles.Teacher] = [rel, rel, same, same, same, same, same],
            [BuiltInRoles.Counselor] = [rel, rel, same, same, same, same, same],
            [BuiltInRoles.Supervisor] = [rel, rel, same, same, same, same, same],
            [BuiltInRoles.Manager] = [deny, same, same, same, same, same, same],
            [BuiltInRoles.OrgAdmin] = [deny, same, same, same, same, same, same],
        };

        foreach (var from in roles)
        {
            for (var i = 0; i < roles.Length; i++)
            {
                Assert.True(table[from][i] == MessagingRuleDefaults.Default(from, roles[i]), $"{from}>{roles[i]} expected {table[from][i]}");
            }
        }
    }
}
