using System.Reflection;
using System.Text.Json;
using Lms.Shared;
using Lms.Shared.Auditing;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;

namespace Lms.UnitTests.Contracts;

/// <summary>M6 wave 4 (Task 4): the moderation contract completed (FR-MSG-04, D66, D69, D70, L16). Only the Org Admin moderates and sees the reported message with its context only.</summary>
public sealed class ModerationContractTests
{
    private static readonly Guid Id = Guid.Parse("0198a6f2-0000-7000-8000-000000000e04");

    private static NotificationEventDefinition Event(string key) => NotificationEvents.Find(key)!;

    private static T Round<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonSerializerOptions.Web), JsonSerializerOptions.Web)!;

    [Fact]
    public void Moderation_catalogs_hold_the_values_of_the_plan_in_sort_order()
    {
        Assert.Equal(["SafetyConcern", "Harassment", "Spam", "Other"], ReportReasons.All); // the order is the sort: a safety concern first
        Assert.Equal(["Open", "InReview", "Dismissed", "ActionTaken"], ReportStatuses.All);
        Assert.Equal(["Dismiss", "Remove", "Warn", "Restrict", "RemoveAndRestrict", "LockConversation"], ModerationActions.All);
        Assert.Equal([1, 7, 30], RestrictionDurations.Days);
        Assert.True(RestrictionDurations.IsValid(7));
        Assert.False(RestrictionDurations.IsValid(0));
        Assert.False(RestrictionDurations.IsValid(14));
        Assert.Equal(["Pending", "Reviewed"], MyReportStatuses.All);
        Assert.Equal(500, MessagingLimits.ReportNoteMaxLength);
    }

    [Fact]
    public void My_report_status_has_only_pending_and_reviewed_and_is_only_on_the_message_for_the_reporter()
    {
        var property = typeof(MessageDto).GetProperty(nameof(MessageDto.MyReportStatus))!;

        Assert.Equal(typeof(string), property.PropertyType);
        Assert.True(new NullabilityInfoContext().Create(property).ReadState == NullabilityState.Nullable); // null for the sender and every other participant
        Assert.Equal(["Pending", "Reviewed"], MyReportStatuses.All); // never an action, a status of the moderator's workflow or a count
        var message = new MessageDto(Id, Id, Id, "س", "S", "نص", MessageStates.Normal, [], DateTime.UtcNow, null, false);
        Assert.Null(message.MyReportStatus);
        Assert.Equal(MyReportStatuses.Reviewed, Round(message with { MyReportStatus = MyReportStatuses.Reviewed }).MyReportStatus);
    }

    [Fact]
    public void Report_resolved_event_carries_no_action()
    {
        var resolved = Event(NotificationEvents.MessagingReportResolved);

        Assert.Equal(["Outcome"], resolved.Parameters);
        Assert.True(resolved.Mandatory);
        Assert.Equal([NotificationChannels.InApp], resolved.DefaultChannels); // email and push follow the preferences; the notice never names an action or a note
        Assert.Equal("Reviewed", ReportOutcomes.Reviewed);
        Assert.Equal(["Reviewed"], ReportOutcomes.All); // the only outcome code a reporter can ever read
        Assert.DoesNotContain(resolved.Parameters, p => p.Contains("Action", StringComparison.OrdinalIgnoreCase) || p.Contains("Note", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Safety_concern_notice_is_mandatory_urgent_and_goes_to_the_org_admins_in_scope()
    {
        var safety = Event(NotificationEvents.ModerationSafetyReportReceived);
        var normal = Event(NotificationEvents.ModerationReportReceived);

        Assert.True(safety.Mandatory);
        Assert.False(normal.Mandatory); // an ordinary report notice can be switched off, a safety concern cannot
        Assert.Equal([BuiltInRoles.OrgAdmin], safety.DefaultRecipientRoles); // D70: only the Org Admin moderates
        Assert.Equal([BuiltInRoles.OrgAdmin], normal.DefaultRecipientRoles);
        Assert.Equal(RecipientScopes.AncestorsAndSelf, safety.RecipientScope); // the admins at or above the report's unit
        Assert.Equal([NotificationChannels.InApp, NotificationChannels.Email, NotificationChannels.Push], safety.DefaultChannels);
        Assert.Equal([NotificationChannels.InApp, NotificationChannels.Email], normal.DefaultChannels);
        Assert.All(new[] { safety, normal }, e => Assert.Equal(["Reason"], e.Parameters)); // the reason code only: no message text, no names
        Assert.True(safety.Urgent); // sent at once, ignoring quiet hours, on push and email
        Assert.False(normal.Urgent);
    }

    [Fact]
    public void The_report_dto_shows_the_frozen_context_and_never_names_the_reporter_or_a_block()
    {
        var detail = typeof(MessageReportDto).GetProperties().Select(p => p.Name).ToList();
        string[] banned = ["Reporter", "Block", "UserId"];
        Assert.DoesNotContain(detail, n => banned.Any(b => n.Contains(b, StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(nameof(MessageReportDto.Context), detail);
        Assert.Contains(nameof(MessageReportDto.SenderSummary), detail);
        Assert.Contains(nameof(MessageReportDto.ServerNowUtc), detail);
        // the frozen context is text and attachment names only: no file id, no user id, no url
        Assert.DoesNotContain(typeof(ReportContextMessageDto).GetProperties(), p => p.PropertyType == typeof(Guid) && p.Name is not "MessageId" || p.Name.Contains("Url", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(typeof(ReportContextMessageDto).GetProperties(), p => p.Name == nameof(ReportContextMessageDto.AttachmentNames));
        Assert.Equal(("PriorReports", "PriorRemovals"), (nameof(SenderSummaryDto.PriorReports), nameof(SenderSummaryDto.PriorRemovals)));
    }

    [Fact]
    public void No_moderation_route_names_a_block_and_every_moderation_route_needs_the_moderate_permission()
    {
        var moderation = typeof(Routes).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Where(f => ((string)f.GetRawConstantValue()!).StartsWith("/api/moderation", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(moderation);
        Assert.DoesNotContain(moderation, f => ((string)f.GetRawConstantValue()!).Contains("block", StringComparison.OrdinalIgnoreCase));
        Assert.All(moderation, f =>
        {
            var doc = RoutesDoc.Summary(f);
            Assert.Contains("Messaging.Moderate", doc, StringComparison.Ordinal); // D70: only the Org Admin holds it, so a Manager is 403 by the role matrix
        });
        Assert.StartsWith("/api/moderation/reports", Routes.ModerationReportsBulkDismiss, StringComparison.Ordinal);
        Assert.Equal($"/api/moderation/restrictions/{Id}", Routes.ModerationRestriction(Id));
        Assert.Equal($"/api/moderation/reports/{Id}/attachments/{Id}", Routes.ModerationReportAttachment(Id, Id));
    }

    [Fact]
    public void Resolve_and_bulk_dismiss_requests_carry_the_durations_and_notes_of_the_plan()
    {
        var resolve = new ResolveReportRequest(ModerationActions.Restrict, 7, "تنبيه");
        var bulk = new BulkDismissRequest([Id, Guid.NewGuid()], null);

        Assert.Equal(7, Round(resolve).DurationDays);
        Assert.Equal("تنبيه", Round(resolve).NoteToPeople);
        Assert.Equal(2, Round(bulk).ReportIds.Count);
        Assert.Equal(50, ModerationLimits.BulkDismissMax);
        Assert.Equal(ModerationActions.Restricting, new[] { ModerationActions.Restrict, ModerationActions.RemoveAndRestrict });
        Assert.DoesNotContain(typeof(ResolveReportRequest).GetProperties(), p => p.Name.Contains("Until", StringComparison.Ordinal)); // a duration in days, never a free date
    }

    [Fact]
    public void Moderation_dtos_round_trip_with_arabic_text()
    {
        var context = new ReportContextMessageDto(Id, "س", "S", "نص الرسالة", ["ملف.pdf"], DateTime.UtcNow, true);
        var summary = new SenderSummaryDto("س", "S", BuiltInRoles.Student, "الصف الأول", "Grade 1", 3, 1);
        var report = new MessageReportDto(Id, Id, Id, ReportReasons.SafetyConcern, "ملاحظة", ReportStatuses.Open, [context], summary, DateTime.UtcNow.AddHours(24), null, null, null, DateTime.UtcNow);
        var item = new MessageReportListItemDto(Id, ReportReasons.Harassment, ReportStatuses.InReview, "س", "S", "ملخص", DateTime.UtcNow, 5, true);
        var stats = new ModerationStatsDto(4, 2, 1, 6.5);
        var restriction = new RestrictionDto(Id, "س", "S", DateTime.UtcNow.AddDays(7), null, DateTime.UtcNow);

        Assert.Equal("نص الرسالة", Round(report).Context.Single().Body);
        Assert.Equal("الصف الأول", Round(report).SenderSummary.ClassNameAr);
        Assert.Equal(("ملخص", true), (Round(item).Snippet, Round(item).IsSafety));
        Assert.Equal((4, 2, 1, 6.5), (Round(stats).Open, Round(stats).NewToday, Round(stats).Restricted, Round(stats).MedianHoursToDecide));
        Assert.Equal("س", Round(restriction).NameAr);
        Assert.Equal(2, new MessageReportQuery().Page + 1);
        Assert.Equal(("Open", null), (new MessageReportQuery(ReportStatuses.Open).Status, new MessageReportQuery().Reason));
    }

    [Fact]
    public void Moderation_settings_have_their_defaults_ranges_and_client_level_scope()
    {
        Assert.Equal(("Moderation.ReportContextRetentionDays", 180), (SettingKeys.ModerationReportContextRetentionDays.Name, SettingKeys.ModerationReportContextRetentionDays.Default));
        Assert.Equal(("Moderation.MaxReportsPerUserPerDay", 20), (SettingKeys.ModerationMaxReportsPerUserPerDay.Name, SettingKeys.ModerationMaxReportsPerUserPerDay.Default));
        Assert.Equal(("Moderation.SafetyReviewHours", 24), (SettingKeys.ModerationSafetyReviewHours.Name, SettingKeys.ModerationSafetyReviewHours.Default));
        foreach (var key in new[] { SettingKeys.ModerationReportContextRetentionDays.Name, SettingKeys.ModerationMaxReportsPerUserPerDay.Name, SettingKeys.ModerationSafetyReviewHours.Name })
        {
            var definition = SettingCatalog.All.Single(d => d.Name == key);
            Assert.True(definition.ClientLevelOnly);
            Assert.Equal(SettingValueKinds.Int, definition.Kind);
            Assert.NotNull(definition.Min);
            Assert.NotNull(definition.Max);
        }

        Assert.Equal((30, 730), (SettingCatalog.All.Single(d => d.Name == SettingKeys.ModerationReportContextRetentionDays.Name).Min, SettingCatalog.All.Single(d => d.Name == SettingKeys.ModerationReportContextRetentionDays.Name).Max));
    }

    [Fact]
    public void The_audit_catalog_lists_reports_restrictions_and_every_moderator_read()
    {
        Assert.Contains("MessageReport", AuditCatalog.EntityTypes);
        Assert.Contains("MessagingRestriction", AuditCatalog.EntityTypes);
        Assert.Contains("ModerationRead", AuditCatalog.EntityTypes);
        Assert.DoesNotContain("UserBlock", AuditCatalog.EntityTypes); // a block belongs to its owner (wave 1 pinned this too)
    }

    private static class RoutesDoc
    {
        public static string Summary(FieldInfo field)
        {
            // The route constants carry their rules in the XML summary; the test project reads them from the source file next to the contract.
            var file = Path.Combine(RepoRoot(), "src", "Lms.Shared", "Routes.Messaging.cs");
            var lines = File.ReadAllLines(file);
            var index = Array.FindIndex(lines, l => l.Contains($"public const string {field.Name} ", StringComparison.Ordinal));
            var doc = new List<string>();
            for (var i = index - 1; i >= 0 && lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal); i--)
            {
                doc.Add(lines[i]);
            }

            return string.Join(' ', doc);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LMS.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
        }
    }
}
