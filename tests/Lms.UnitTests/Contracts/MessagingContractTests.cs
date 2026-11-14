using System.Text.Json;
using System.Text.RegularExpressions;
using Lms.Infrastructure.Access;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;

namespace Lms.UnitTests.Contracts;

/// <summary>M6 wave 1 (#613 revision 4): permissions and role defaults, the D69 rules matrix, catalogs, DTOs, routes, events and settings.</summary>
public sealed class MessagingContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static IReadOnlySet<string> Perms(string role) => BuiltInRoles.Get(role).DefaultPermissions.ToHashSet();

    [Fact]
    public void Permissions_follow_the_roles_of_the_proposal()
    {
        string[] all = [Permissions.Messaging.View, Permissions.Messaging.Send, Permissions.Messaging.Moderate, Permissions.Announcements.View, Permissions.Announcements.Manage, Permissions.Comms.Manage];
        Assert.Equal(all.Order(), Permissions.All.Where(p => p.StartsWith("Messaging.") || p.StartsWith("Announcements.") || p.StartsWith("Comms.")).Order());

        Assert.All(all, p => Assert.Contains(p, Perms(BuiltInRoles.OrgAdmin)));
        Assert.DoesNotContain(Permissions.Messaging.Moderate, Perms(BuiltInRoles.Manager)); // moderation and the communication settings are the Org Admin's
        Assert.DoesNotContain(Permissions.Comms.Manage, Perms(BuiltInRoles.Manager));
        Assert.Contains(Permissions.Announcements.Manage, Perms(BuiltInRoles.Manager));
        Assert.Contains(Permissions.Announcements.Manage, Perms(BuiltInRoles.Supervisor));
        Assert.Contains(Permissions.Announcements.Manage, Perms(BuiltInRoles.Teacher));
        Assert.DoesNotContain(Permissions.Announcements.Manage, Perms(BuiltInRoles.Counselor)); // the counselor reads announcements only
        foreach (var role in new[] { BuiltInRoles.Student, BuiltInRoles.Guardian })
        {
            Assert.Contains(Permissions.Messaging.Send, Perms(role));
            Assert.Contains(Permissions.Announcements.View, Perms(role));
            Assert.DoesNotContain(Permissions.Announcements.Manage, Perms(role));
            Assert.DoesNotContain(Permissions.Messaging.Moderate, Perms(role));
        }

        // a Teacher announces only to own ClassCourses and their groups
        Assert.Contains(Permissions.Announcements.Manage, RelationshipRules.All.Single(r => r.RoleKey == BuiltInRoles.Teacher).Permissions);
        // no PlatformOnly or SelfService leak: messaging is held by staff too, so the escalation guards count it for everyone
        Assert.DoesNotContain(Permissions.Messaging.Send, Permissions.SelfService);
    }

    [Fact]
    public void Defaults_version_11_introduces_exactly_the_six_permissions()
    {
        Assert.Equal(11, BuiltInRoleDefaults.CurrentVersion);
        Assert.Equal(6, BuiltInRoleDefaults.IntroducedInVersion11.Count);
        Assert.All(BuiltInRoleDefaults.IntroducedInVersion11, p => Assert.Equal(11, BuiltInRoleDefaults.IntroducedIn(p)));
    }

    // --- the D69 rules matrix

    [Fact]
    public void Default_matrix_is_safeguarding_first()
    {
        string D(string from, string to) => MessagingRuleDefaults.Default(from, to);

        Assert.Equal(MessagingRuleModes.Deny, D(BuiltInRoles.Student, BuiltInRoles.Student)); // never another student
        Assert.Equal(MessagingRuleModes.Relationship, D(BuiltInRoles.Student, BuiltInRoles.Teacher));
        Assert.Equal(MessagingRuleModes.Relationship, D(BuiltInRoles.Student, BuiltInRoles.Counselor));
        Assert.Equal(MessagingRuleModes.Deny, D(BuiltInRoles.Student, BuiltInRoles.Manager));
        Assert.Equal(MessagingRuleModes.Deny, D(BuiltInRoles.Guardian, BuiltInRoles.Guardian)); // never another guardian
        Assert.Equal(MessagingRuleModes.Deny, D(BuiltInRoles.Guardian, BuiltInRoles.Student));
        Assert.Equal(MessagingRuleModes.Relationship, D(BuiltInRoles.Guardian, BuiltInRoles.Teacher));
        Assert.Equal(MessagingRuleModes.Relationship, D(BuiltInRoles.Guardian, BuiltInRoles.Counselor));
        Assert.Equal(MessagingRuleModes.Deny, D(BuiltInRoles.Guardian, BuiltInRoles.OrgAdmin));
        Assert.Equal(MessagingRuleModes.Relationship, D(BuiltInRoles.Teacher, BuiltInRoles.Student)); // their own students
        Assert.Equal(MessagingRuleModes.Relationship, D(BuiltInRoles.Teacher, BuiltInRoles.Guardian));
        Assert.Equal(MessagingRuleModes.Allow, D(BuiltInRoles.Teacher, BuiltInRoles.Teacher));
        Assert.Equal(MessagingRuleModes.Relationship, D(BuiltInRoles.Counselor, BuiltInRoles.Student));
        Assert.Equal(MessagingRuleModes.Relationship, D(BuiltInRoles.Supervisor, BuiltInRoles.Guardian));
        // managers and Org Admins: no private one-to-one with a student (groups and announcements only)
        Assert.Equal(MessagingRuleModes.Deny, D(BuiltInRoles.Manager, BuiltInRoles.Student));
        Assert.Equal(MessagingRuleModes.Deny, D(BuiltInRoles.OrgAdmin, BuiltInRoles.Student));
        Assert.Equal(MessagingRuleModes.Allow, D(BuiltInRoles.Manager, BuiltInRoles.Guardian));
        Assert.Equal(MessagingRuleModes.Allow, D(BuiltInRoles.OrgAdmin, BuiltInRoles.Teacher));
        Assert.Equal(MessagingRuleModes.Deny, D("Custom", BuiltInRoles.Teacher)); // an unknown role is denied
    }

    [Fact]
    public void The_matrix_has_one_cell_per_pair_and_the_defaults_are_the_only_unrestricted_loosest_state()
    {
        var matrix = MessagingRuleDefaults.Matrix();

        Assert.Equal(49, matrix.Count);
        Assert.DoesNotContain(matrix, c => c.Mode == MessagingRuleModes.SameUnit); // no default uses SameUnit
        Assert.Equal(49, matrix.Select(c => (c.FromRole, c.ToRole)).Distinct().Count());
        Assert.Empty(MessagingRuleDefaults.Loosened(matrix)); // the defaults themselves are valid
    }

    [Fact]
    public void Rules_cannot_be_loosened_below_the_defaults()
    {
        // every denied default cell refuses Relationship and Allow; every Relationship cell refuses Allow
        foreach (var cell in MessagingRuleDefaults.Matrix())
        {
            foreach (var mode in MessagingRuleModes.All)
            {
                var looser = MessagingRuleModes.Rank(mode) > MessagingRuleModes.Rank(cell.Mode);
                var result = MessagingRuleDefaults.Loosened([cell with { Mode = mode }]);

                Assert.Equal(looser, result.Count == 1);
            }
        }

        // the headline cases of D69
        Assert.Single(MessagingRuleDefaults.Loosened([new(BuiltInRoles.Student, BuiltInRoles.Student, MessagingRuleModes.Allow)]));
        Assert.Single(MessagingRuleDefaults.Loosened([new(BuiltInRoles.Manager, BuiltInRoles.Student, MessagingRuleModes.Relationship)]));
        Assert.Single(MessagingRuleDefaults.Loosened([new(BuiltInRoles.Guardian, BuiltInRoles.Guardian, MessagingRuleModes.Allow)]));
        Assert.Single(MessagingRuleDefaults.Loosened([new(BuiltInRoles.Guardian, BuiltInRoles.OrgAdmin, MessagingRuleModes.Allow)]));
        // narrowing and restoring are accepted; an unknown role or mode is refused
        Assert.Empty(MessagingRuleDefaults.Loosened([new(BuiltInRoles.Teacher, BuiltInRoles.Teacher, MessagingRuleModes.Deny)]));
        Assert.Empty(MessagingRuleDefaults.Loosened([new(BuiltInRoles.Teacher, BuiltInRoles.Student, MessagingRuleModes.Relationship)]));
        Assert.Single(MessagingRuleDefaults.Loosened([new("Hacker", BuiltInRoles.Teacher, MessagingRuleModes.Deny)]));
        Assert.Single(MessagingRuleDefaults.Loosened([new(BuiltInRoles.Teacher, BuiltInRoles.Teacher, "Maybe")]));
        // an allowed cell cannot be widened either: a teacher reaching any student of the school is refused (Relationship -> SameUnit -> Anywhere)
        Assert.Single(MessagingRuleDefaults.Loosened([new(BuiltInRoles.Teacher, BuiltInRoles.Student, MessagingRuleModes.SameUnit)]));
        Assert.Single(MessagingRuleDefaults.Loosened([new(BuiltInRoles.Teacher, BuiltInRoles.Student, MessagingRuleModes.Allow)]));
        Assert.Equal([0, 1, 2, 3], MessagingRuleModes.All.Select(MessagingRuleModes.Rank));
        Assert.Equal(ProblemCodes.rules_looser_than_default, nameof(ProblemCodes.rules_looser_than_default));
    }

    // --- catalogs, limits, settings, events

    [Fact]
    public void Catalogs_hold_the_values_of_the_proposal()
    {
        Assert.Equal(["Direct", "Group"], ConversationKinds.All);
        Assert.Equal(["Normal", "Deleted", "Removed"], MessageStates.All);
        Assert.Equal(["Dismiss", "Remove", "Warn", "Restrict", "RemoveAndRestrict", "LockConversation"], ModerationActions.All);
        Assert.Equal(["Restrict", "RemoveAndRestrict"], ModerationActions.Restricting);
        Assert.Equal(["Open", "InReview", "Dismissed", "ActionTaken"], ReportStatuses.All);
        Assert.Equal(["Organization", "Unit", "Level", "Class", "ClassCourse", "Group"], AnnouncementTargetKinds.All);
        Assert.Equal(["Draft", "Scheduled", "Published", "Withdrawn", "Expired"], AnnouncementStatuses.All);
        Assert.Equal(["Staff", "Students", "Guardians"], AnnouncementRoleFilters.All);
        Assert.Equal(["Active", "NeedsSetup", "Blocked", "Bounced"], ChannelStates.All);
        Assert.Equal(["Pending", "Sending", "Sent", "Failed", "Skipped", "Expired"], DeliveryStatuses.All);
        Assert.Equal((15, 5, 10, 50, 2, 50, 200), (MessagingLimits.DeleteWindowMinutes, MessagingLimits.MaxAttachmentsPerMessage, MessagingLimits.MaxAttachmentMb,
            MessagingLimits.DefaultMaxGroupSize, MessagingLimits.SearchMinLength, MessagingLimits.SearchMaxResults, MessagingLimits.SearchWindow));
    }

    [Fact]
    public void Settings_carry_the_d66_defaults_and_stay_client_level()
    {
        Assert.Equal((true, false), (SettingKeys.CommsEmailEnabled.Default, SettingKeys.CommsPushEnabled.Default));
        Assert.Equal((5000, 20000), (SettingKeys.CommsMaxEmailPerDay.Default, SettingKeys.CommsMaxPushPerDay.Default));
        Assert.Equal((20, 50), (SettingKeys.CommsMaxEmailPerUserPerDay.Default, SettingKeys.CommsMaxPushPerUserPerDay.Default));
        Assert.Equal((false, "21:00", "07:00"), (SettingKeys.CommsQuietHoursEnabled.Default, SettingKeys.CommsQuietHoursStart.Default, SettingKeys.CommsQuietHoursEnd.Default));
        Assert.Equal((90, false, 50, 10), (SettingKeys.CommsDeliveryRetentionDays.Default, SettingKeys.MessagingReadReceipts.Default,
            SettingKeys.MessagingMaxGroupSize.Default, SettingKeys.AnnouncementsMaxPerAuthorPerDay.Default));
        var commsSettings = SettingCatalog.All.Where(d => d.Name.StartsWith("Comms.") || d.Name.StartsWith("Messaging.") || d.Name.StartsWith("Announcements.")).ToList();
        Assert.Equal(15, commsSettings.Count);
        Assert.All(commsSettings, d => Assert.True(d.ClientLevelOnly, d.Name)); // only the Org Admin at the client root edits them
        Assert.All(SettingCatalog.All.Where(d => d.Kind == SettingValueKinds.Int), d => Assert.True(d.Min is not null && d.Max is not null, d.Name));
    }

    [Fact]
    public void Message_events_carry_no_message_text_and_the_moderation_notices_are_mandatory()
    {
        var keys = new[]
        {
            NotificationEvents.MessagesNewMessage, NotificationEvents.AnnouncementsPublished, NotificationEvents.MessagingWarning,
            NotificationEvents.MessagingRestricted, NotificationEvents.MessagingReportResolved, NotificationEvents.CommsCapReached,
        };
        var definitions = keys.Select(k => NotificationEvents.Find(k)!).ToList();

        Assert.All(definitions, d => Assert.DoesNotContain(d.Parameters, p => p.Contains("Body", StringComparison.Ordinal) || p.Contains("Text", StringComparison.Ordinal) || p.Contains("Message", StringComparison.Ordinal)));
        Assert.Equal(["SenderNameAr", "SenderNameEn"], NotificationEvents.Find(NotificationEvents.MessagesNewMessage)!.Parameters);
        Assert.Equal(["TitleAr", "TitleEn"], NotificationEvents.Find(NotificationEvents.AnnouncementsPublished)!.Parameters); // a title, never the body
        Assert.All(new[] { NotificationEvents.MessagingWarning, NotificationEvents.MessagingRestricted, NotificationEvents.MessagingReportResolved },
            k => Assert.True(NotificationEvents.Find(k)!.Mandatory, k)); // moderation outcomes cannot be switched off (D66)
        Assert.False(NotificationEvents.Find(NotificationEvents.MessagesNewMessage)!.Mandatory);
        Assert.Equal([BuiltInRoles.OrgAdmin], NotificationEvents.Find(NotificationEvents.CommsCapReached)!.DefaultRecipientRoles);
        Assert.Null(NotificationEvents.Find(NotificationEvents.GradesPublished)!.DefaultChannels); // existing events are unchanged
    }

    [Fact]
    public void The_realtime_event_and_the_student_facing_dtos_carry_ids_only_or_no_staff_data()
    {
        // "ids only, never text": the realtime event has no string member at all
        Assert.All(typeof(MessageReceivedEvent).GetProperties(), p => Assert.Equal(typeof(Guid), p.PropertyType));
        // a participant's message shapes never carry the moderator-only fields
        Type[] participantFacing = [typeof(MessageDto), typeof(ConversationDto), typeof(ConversationListItemDto), typeof(MessageSearchResultDto)];
        string[] moderatorOnly = ["Original", "Reporter", "Reported", "Resolution", "Restrict"];
        Assert.All(participantFacing, t => Assert.DoesNotContain(t.GetProperties(), p => moderatorOnly.Any(m => p.Name.Contains(m, StringComparison.OrdinalIgnoreCase))));
        // a block is the owner's: no moderation DTO mentions one
        Assert.DoesNotContain(new[] { typeof(MessageReportDto), typeof(ResolveReportRequest), typeof(RestrictionDto), typeof(ModerationStatsDto) }
            .SelectMany(t => t.GetProperties()), p => p.Name.Contains("Block", StringComparison.OrdinalIgnoreCase));
        // an announcement recipient never sees audience sizes or authors' targets
        Assert.DoesNotContain(typeof(MyAnnouncementDto).GetProperties(), p => p.Name is "AudienceCount" or "Targets" or "Status");
    }

    [Fact]
    public void The_group_contract_has_a_read_only_switch_owner_member_changes_and_a_class_course_input()
    {
        var group = new ConversationDto(Id, ConversationKinds.Group, "فصل", [], false, false, DateTime.UtcNow, IsReadOnly: true, OwnerUserId: Id, ClassCourseId: Id);

        Assert.True(Round(group).IsReadOnly);
        Assert.False(Round(group).IsLocked); // read-only is not the moderator's lock
        Assert.Equal(Id, Round(group).ClassCourseId);
        Assert.Equal(Id, Round(new CreateConversationRequest(ConversationKinds.Group, [Id, Id], "فصل", null, Id)).ClassCourseId);
        Assert.Equal([Id], Round(new AddParticipantsRequest([Id])).UserIds);
        Assert.False(new ConversationDto(Id, ConversationKinds.Direct, null, [], false, false, DateTime.UtcNow).IsReadOnly); // older shapes default to writable
        Assert.Equal("group_read_only", ProblemCodes.group_read_only); // D74: students never post in a group
    }

    [Fact]
    public void There_is_no_sms_in_v1_anywhere_in_the_contract()
    {
        // D76: the channels are in-app, email and push; no SMS setting, cap, DTO field or event default remains (this also settles D72 and D75)
        Assert.Equal(["InApp", "Email", "Push"], NotificationChannels.All);
        Assert.DoesNotContain(SettingCatalog.All, d => d.Name.Contains("Sms", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(CommsSettingsDto).GetProperties(), p => p.Name.Contains("Sms", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(NotificationEvents.All.SelectMany(e => e.DefaultChannels ?? []), c => c.Contains("Sms", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(ProblemCodes).GetFields().Select(f => f.Name), n => n.Contains("sms", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(NotificationChannels).GetFields().Select(f => f.Name), n => n == "Sms");
    }

    [Fact]
    public void Groups_are_read_only_for_students_and_open_to_guardians_of_teacher_run_groups()
    {
        // D74 (students never post in a group) and D77 (guardians may post in teacher-run groups that include guardians) live in one code and the route docs
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lms.Shared", "Routes.Messaging.cs"));

        Assert.Equal("group_read_only", ProblemCodes.group_read_only);
        Assert.Contains("D74", text, StringComparison.Ordinal);
        Assert.Contains("D77", text, StringComparison.Ordinal);
        Assert.Contains("guardian", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Announcements_carry_an_urgent_flag_and_a_capability_dto_and_urgent_means_no_sms()
    {
        var create = new CreateAnnouncementRequest(Id, "عاجل", "Urgent", "نص", null, [new AnnouncementTargetDto(AnnouncementTargetKinds.Organization, null, true)],
            [AnnouncementRoleFilters.Guardians], null, null, true, true, null, true, IsUrgent: true);
        var update = new UpdateAnnouncementRequest("عاجل", "Urgent", null, null, [], null, null, null, true, true, null, IsUrgent: true);

        Assert.True(Round(create).IsUrgent);
        Assert.True(Round(update).IsUrgent);
        Assert.False(new MyAnnouncementDto(Id, "ع", "E", null, null, [], "م", "A", DateTime.UtcNow, false, false, null).IsUrgent); // older shapes default to not urgent
        Assert.Equal((true, false, true), (Round(new AnnouncementCapabilitiesDto(true, false, true)).CanAuthorUnitAnnouncements,
            Round(new AnnouncementCapabilitiesDto(true, false, true)).CanAuthorClassCourseAnnouncements, Round(new AnnouncementCapabilitiesDto(true, false, true)).CanMarkUrgent));
        Assert.StartsWith("/api/", Routes.OrgAnnouncementCapabilities, StringComparison.Ordinal);
        // D76: urgent is push and email, immediately; the doc says so and no SMS channel exists to fall back on
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lms.Shared", "Messaging", "AnnouncementDtos.cs"));
        Assert.Contains("IsUrgent", text, StringComparison.Ordinal);
        Assert.Contains("push", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(NotificationChannels.All, c => c.Equals("Sms", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Dtos_round_trip_with_arabic_text()
    {
        var participant = new ParticipantDto(Id, "سارة", "Sara", BuiltInRoles.Teacher, false);
        var message = new MessageDto(Id, Id, Id, "سارة", "Sara", "مرحبا", MessageStates.Normal, [new MessageAttachmentDto(Id, "ملف.pdf", "application/pdf", 10)], DateTime.UtcNow, null, true);
        var conversation = new ConversationDto(Id, ConversationKinds.Group, "مجموعة", [participant], false, true, DateTime.UtcNow);
        var create = new CreateConversationRequest(ConversationKinds.Direct, [Id], null, "أهلا");
        var announcement = new CreateAnnouncementRequest(Id, "عنوان", "Title", "نص", null, [new AnnouncementTargetDto(AnnouncementTargetKinds.Unit, Id, true)],
            [AnnouncementRoleFilters.Guardians], DateTime.UtcNow.AddDays(1), null, true, true, [Id], true);
        var report = new MessageReportDto(Id, Id, Id, ReportReasons.Harassment, "ملاحظة", ReportStatuses.Open, [new ReportContextMessageDto(Id, "س", "S", "أصل", [], DateTime.UtcNow, true)], new SenderSummaryDto("س", "S", BuiltInRoles.Student, null, null, 0, 0), null, null, null, null, DateTime.UtcNow);
        var rules = new MessagingRulesDto(MessagingRuleDefaults.Matrix(), MessagingRuleDefaults.Matrix());
        var settings = new CommsSettingsDto(true, false, 5000, 20000, 20, 50, false, "21:00", "07:00", 90, false, 30, 20, 50, 10);
        var preference = new EffectivePreferenceDto(NotificationEvents.GradesPublished, NotificationChannels.Email, true, PreferenceSources.Default, false);

        Assert.Equal("مرحبا", Round(message).Body);
        Assert.Equal("مجموعة", Round(conversation).Title);
        Assert.Equal(create.ParticipantUserIds, Round(create).ParticipantUserIds);
        Assert.Equal(AnnouncementTargetKinds.Unit, Round(announcement).Targets.Single().Kind);
        Assert.Equal("أصل", Round(report).Context.Single().Body);
        Assert.Equal(49, Round(rules).Cells.Count);
        Assert.Equal(("21:00", 5000), (Round(settings).QuietHoursStart, Round(settings).MaxEmailPerDay));
        Assert.Equal(PreferenceSources.Default, Round(preference).Source);
        Assert.Equal(MessagingLimits.MessagePageSize, new MessageQuery().PageSize);
    }

    // --- routes

    [Fact]
    public void Routes_are_api_routes_and_unique_and_the_builders_match_the_patterns()
    {
        var patterns = typeof(Routes).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!).ToList();
        var messaging = new[]
        {
            Routes.Conversations, Routes.ConversationPattern, Routes.ConversationMessagesPattern, Routes.MessagePattern, Routes.ConversationReadPattern, Routes.ConversationMutePattern, Routes.ConversationLeavePattern, Routes.ConversationParticipantsPattern, Routes.ConversationParticipantPattern, Routes.ConversationReadOnlyPattern, Routes.MessageSearch,
            Routes.MessageReportPattern, Routes.MyBlocks, Routes.MyBlockPattern, Routes.MessagingRecipients, Routes.MessagingRulesSummary, Routes.MessagingLimits,
            Routes.MyUnreadTotal, Routes.ModerationReports, Routes.ModerationReportPattern, Routes.ModerationReportResolvePattern, Routes.ModerationStats,
            Routes.ModerationReportsBulkDismiss, Routes.ModerationReportAttachmentPattern, Routes.ModerationRestrictions, Routes.ModerationRestrictionPattern,
            Routes.OrgAnnouncements, Routes.OrgAnnouncementPattern, Routes.OrgAnnouncementPublishPattern, Routes.OrgAnnouncementWithdrawPattern,
            Routes.OrgAnnouncementAudiencePreview, Routes.OrgAnnouncementCapabilities, Routes.MyAnnouncements, Routes.MyAnnouncementReadPattern, Routes.MyPushSubscriptions, Routes.MyPushSubscriptionPattern,
            Routes.MyEffectivePreferences, Routes.MyChannels, Routes.MyEmailTest, Routes.CommsMessagingRules, Routes.CommsProviderStatus, Routes.CommsSettings,
            Routes.EmailEventsWebhook, Routes.DevCommsOutbox,
        };

        Assert.All(messaging, r => Assert.StartsWith("/api/", r, StringComparison.Ordinal));
        Assert.Equal(messaging.Length, messaging.Distinct().Count());
        Assert.Empty(patterns.GroupBy(p => p, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key)); // no clash with the M4a course announcements or any other route

        var c = Guid.NewGuid();
        var m = Guid.NewGuid();
        Assert.Equal($"/api/conversations/{c}/messages/{m}", Routes.Message(c, m));
        Assert.Equal($"/api/conversations/{c}/messages?pageSize=50", Routes.ConversationMessages(c, null));
        Assert.Equal($"/api/conversations/{c}/messages?beforeId={m}&pageSize=20", Routes.ConversationMessages(c, m, 20));
        Assert.Equal($"/api/conversations/{c}/mute", Routes.ConversationMute(c));
        Assert.Equal($"/api/conversations/{c}/leave", Routes.ConversationLeave(c));
        Assert.Equal($"/api/conversations/{c}/participants/{m}", Routes.ConversationParticipant(c, m));
        Assert.Equal($"/api/conversations/{c}/read-only", Routes.ConversationReadOnly(c));
        Assert.Equal(new MuteConversationRequest(null), Round(new MuteConversationRequest(null)));
        Assert.Equal($"/api/messages/{m}/report", Routes.MessageReport(m));
        Assert.Equal($"/api/comms/announcements/{c}/publish", Routes.OrgAnnouncementPublish(c));
        Assert.Equal($"/api/moderation/reports/{c}/resolve", Routes.ModerationReportResolve(c));
        Assert.Equal("/api/messages/search?q=%D9%85%D8%B1%D8%AD%D8%A8%D8%A7&page=1", Routes.MessageSearchUrl("مرحبا"));
    }

    [Fact]
    public void Every_messaging_route_documents_its_permission_or_who_may_call_it()
    {
        // The doc comment of each route constant is its row of the proposal's route table; the probe suite tests every row against it.
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lms.Shared", "Routes.Messaging.cs"));
        var matches = Regex.Matches(text, @"((?:\s*///[^\n]*\n)+)\s*public const string (\w+) =");
        var routes = matches.Select(m => (Name: m.Groups[2].Value, Doc: m.Groups[1].Value)).ToList();

        Assert.True(routes.Count >= 40, $"found {routes.Count} documented route constants");
        Assert.All(routes, r => Assert.Matches(@"Messaging\.\w+|Announcements\.\w+|Comms\.\w+|authenticated|anonymous|Development only|participant", r.Doc));
        Assert.Equal(Regex.Matches(text, @"public const string \w+ =").Count, routes.Count);
    }

    private static T Round<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Web), Web)!;

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LMS.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (LMS.slnx) not found above " + AppContext.BaseDirectory);
    }
}
