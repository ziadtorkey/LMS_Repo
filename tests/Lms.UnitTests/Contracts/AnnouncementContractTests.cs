using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Lms.Infrastructure.Access;
using Lms.Shared;
using Lms.Shared.Auditing;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;

namespace Lms.UnitTests.Contracts;

/// <summary>M6 wave 3 (plan Task 3, FR-MSG-03, D66): announcement scheduling, urgency, audience preview and attachments on top of #724.</summary>
public sealed class AnnouncementContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly Guid Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Type[] AnnouncementShapes =
    [
        typeof(AnnouncementDto), typeof(AnnouncementListItemDto), typeof(CreateAnnouncementRequest), typeof(UpdateAnnouncementRequest),
        typeof(AnnouncementTargetDto), typeof(AudiencePreviewRequest), typeof(AudiencePreviewDto), typeof(AnnouncementAttachmentUploadDto),
        typeof(AnnouncementDeliverySummaryDto), typeof(MyAnnouncementDto), typeof(AnnouncementCapabilitiesDto),
    ];

    [Fact]
    public void Teacher_manage_is_relationship_bound()
    {
        // Announcements.Manage is a built-in default of the Teacher, but only through the OwnClassCourses relationship rule, never unit-wide.
        var rule = RelationshipRules.All.Single(r => r.RoleKey == BuiltInRoles.Teacher);
        Assert.Equal(Relationships.OwnClassCourses, rule.Relationship);
        Assert.Contains(Permissions.Announcements.Manage, rule.Permissions);
        Assert.Contains(Permissions.Announcements.View, BuiltInRoles.Get(BuiltInRoles.Teacher).DefaultPermissions);
        Assert.Equal(11, BuiltInRoleDefaults.CurrentVersion); // wave 3 adds no permission
    }

    [Fact]
    public void Both_titles_are_required_in_the_contract()
    {
        var nullability = new NullabilityInfoContext();
        foreach (var type in new[] { typeof(AnnouncementDto), typeof(CreateAnnouncementRequest), typeof(UpdateAnnouncementRequest), typeof(MyAnnouncementDto), typeof(AnnouncementListItemDto) })
        {
            foreach (var name in new[] { "TitleAr", "TitleEn" })
            {
                var property = type.GetProperty(name);
                Assert.NotNull(property);
                Assert.Equal(NullabilityState.NotNull, nullability.Create(property!).ReadState);
            }
        }

        var update = new UpdateAnnouncementRequest("عاجل", "Urgent", null, null, [], null, null, null, false, true, null);
        Assert.Equal(("عاجل", "Urgent"), (Round(update).TitleAr, Round(update).TitleEn));
        Assert.Equal(AnnouncementTargetKinds.All, ["Organization", "Unit", "Level", "Class", "ClassCourse", "Group"]);
        Assert.Equal(AnnouncementStatuses.All, ["Draft", "Scheduled", "Published", "Withdrawn", "Expired"]);
        Assert.Equal(["Announcement", "CourseBoard"], AnnouncementSources.All.Order()); // L15: the plan's names
        Assert.Equal(AnnouncementSources.Announcement, new MyAnnouncementDto(Id, "ع", "E", null, null, [], "م", "A", DateTime.UtcNow, false, false, null).Source);
    }

    [Fact]
    public void No_announcement_body_in_notification_params()
    {
        var definition = NotificationEvents.Find(NotificationEvents.AnnouncementsPublished)!;
        Assert.Equal(NotificationEvents.AnnouncementsPublished, "Announcements.Published");
        Assert.Equal(["TitleAr", "TitleEn"], definition.Parameters); // titles in both languages, never the body
        Assert.DoesNotContain(definition.Parameters, p => p.Contains("Body", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Announcement_contract_has_no_sms_field()
    {
        // D76: no SMS anywhere in v1, in a DTO field, a route, a setting or a catalog value.
        Assert.All(AnnouncementShapes, t => Assert.DoesNotContain(t.GetProperties(), p => p.Name.Contains("Sms", StringComparison.OrdinalIgnoreCase)));
        Assert.All(AnnouncementShapes, t => Assert.DoesNotContain(t.GetConstructors().Single().GetParameters(), p => p.Name!.Contains("Sms", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(AnnouncementRouteConstants(), f => f.Name.Contains("Sms", StringComparison.OrdinalIgnoreCase) || ((string)f.GetRawConstantValue()!).Contains("sms", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(SettingCatalog.All, d => d.Name.StartsWith("Announcements.", StringComparison.Ordinal) && d.Name.Contains("Sms", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(AnnouncementRoleFilters.All.Concat(AnnouncementStatuses.All), v => v.Contains("Sms", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Announcement_attachment_routes_have_permission_doc_comments()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lms.Shared", "Routes.Messaging.cs"));
        string[] attachmentRoutes = [nameof(Routes.OrgAnnouncementAttachments), nameof(Routes.OrgAnnouncementAttachmentPattern)];
        foreach (var name in attachmentRoutes)
        {
            var index = text.IndexOf($"string {name} =", StringComparison.Ordinal);
            Assert.True(index > 0, name + " is missing");
            var doc = text[text.LastIndexOf("/// <summary>", index, StringComparison.Ordinal)..index];
            Assert.Contains("Announcements.Manage", doc, StringComparison.Ordinal);
        }

        // every announcement route stays under /api/comms/announcements, except the reader feed of the signed-in user
        Assert.All(AnnouncementRouteConstants().Where(f => !f.Name.StartsWith("My", StringComparison.Ordinal)),
            f => Assert.StartsWith("/api/comms/announcements", (string)f.GetRawConstantValue()!, StringComparison.Ordinal));
        Assert.StartsWith(Routes.OrgAnnouncements + "/", Routes.OrgAnnouncementAttachments, StringComparison.Ordinal);
        Assert.Equal($"{Routes.OrgAnnouncementAttachments}/{Id}", Routes.OrgAnnouncementAttachment(Id));
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Every_problem_code_named_in_the_announcement_route_docs_exists_and_is_labelled(string culture)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lms.Shared", "Routes.Messaging.cs"));
        var announcementDocs = text[text.IndexOf("string OrgAnnouncements =", StringComparison.Ordinal)..];
        var named = System.Text.RegularExpressions.Regex.Matches(announcementDocs, "<c>([a-z]+(?:_[a-z]+)+)</c>").Select(m => m.Groups[1].Value).Distinct().ToList();
        var codes = typeof(ProblemCodes).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToHashSet();
        var resx = XDocument.Load(Path.Combine(RepoRoot(), "src", "Lms.Client", "Resources", $"SharedResource.{culture}.resx"));
        var labels = resx.Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();

        Assert.Contains("attachment_invalid", named);
        Assert.Contains("rate_limited", named);
        Assert.All(named, c => Assert.True(codes.Contains(c), "ProblemCodes lacks " + c));
        Assert.All(named, c => Assert.True(labels.Contains("Error_" + c), $"{culture}: no label for {c}"));
    }

    [Fact]
    public void Preview_delivery_summary_and_upload_shapes_round_trip()
    {
        var chips = new Dictionary<string, int> { [AnnouncementRoleFilters.Students] = 120, [AnnouncementRoleFilters.Guardians] = 90 };
        var preview = Round(new AudiencePreviewDto(210, chips, ["سارة", "Sara"]));
        var upload = Round(new AnnouncementAttachmentUploadDto(Id, "جدول.pdf", "application/pdf", 2048));
        var summary = new AnnouncementDeliverySummaryDto([new AnnouncementDeliveryCountDto(NotificationChannels.Email, DeliveryStatuses.Sent, 80)]);
        var now = DateTime.UtcNow;
        var dto = Round(new AnnouncementDto(Id, Id, "عنوان", "Title", null, null, AnnouncementStatuses.Scheduled, [], [], now.AddHours(1), null, null, false, true, [],
            Id, "م", "A", null, null, now, summary, now, IsUrgent: true));

        Assert.Equal((210, 120), (preview.Count, preview.CountsByChip[AnnouncementRoleFilters.Students]));
        Assert.Equal(("جدول.pdf", 2048L), (upload.FileName, upload.SizeBytes));
        Assert.Equal(80, dto.DeliverySummary!.Counts.Single().Count);
        Assert.Equal((true, now.AddHours(1)), (dto.IsUrgent, dto.PublishAtUtc));
        Assert.Equal(now, dto.ServerNowUtc);
        Assert.True(MessagingLimits.AudiencePreviewSampleMax <= 10);
    }

    [Fact]
    public void Announcement_settings_follow_the_plan_and_have_no_new_permission()
    {
        Assert.Equal((10, 5, 10), (SettingKeys.AnnouncementsMaxPerAuthorPerDay.Default, SettingKeys.AnnouncementsMaxAttachments.Default, SettingKeys.AnnouncementsUploadsPerMinute.Default));
        Assert.Equal("Announcements.MaxAttachments", SettingKeys.AnnouncementsMaxAttachments.Name);
        Assert.Equal("Announcements.UploadsPerMinute", SettingKeys.AnnouncementsUploadsPerMinute.Name);
        Assert.Equal(MessagingLimits.MaxAnnouncementAttachments, SettingKeys.AnnouncementsMaxAttachments.Default);
        Assert.All(new[] { SettingKeys.AnnouncementsMaxAttachments.Name, SettingKeys.AnnouncementsUploadsPerMinute.Name },
            n => Assert.True(SettingCatalog.All.Single(d => d.Name == n).ClientLevelOnly));
        Assert.Equal(6, Permissions.All.Count(p => p.StartsWith("Messaging.") || p.StartsWith("Announcements.") || p.StartsWith("Comms."))); // unchanged since wave 1
        Assert.Contains("Announcement", AuditCatalog.EntityTypes);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Every_announcement_label_exists_in_ar_and_en(string culture)
    {
        var resx = XDocument.Load(Path.Combine(RepoRoot(), "src", "Lms.Client", "Resources", $"SharedResource.{culture}.resx"));
        var values = resx.Root!.Elements("data").ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value"));
        var keys = new List<string>
        {
            "Setting_Announcements_MaxPerAuthorPerDay", "Setting_Announcements_MaxAttachments", "Setting_Announcements_UploadsPerMinute",
            "Error_announcement_target_invalid", "Error_announcement_not_editable", "AuditEntity_Announcement",
            "NotifEvent_Announcements_Published", "Notif_Announcements_Published",
        };
        keys.AddRange(AnnouncementSources.All.Select(v => "Ann_Source_" + v));
        keys.AddRange(AnnouncementStatuses.All.Select(v => "Ann_Status_" + v));
        keys.AddRange(AnnouncementTargetKinds.All.Select(v => "Ann_TargetKind_" + v));
        keys.AddRange(AnnouncementRoleFilters.All.Select(v => "Ann_Role_" + v));

        Assert.All(keys, k => Assert.False(string.IsNullOrWhiteSpace(values.GetValueOrDefault(k)), $"{culture}: missing resource {k}"));
        Assert.DoesNotContain("Ann_Source_School", values.Keys); // renamed to Announcement (L15)
        Assert.DoesNotContain(values.Keys, k => k.Contains("Sms", StringComparison.OrdinalIgnoreCase) && k.Contains("Announcement", StringComparison.OrdinalIgnoreCase));
    }

    // the M6 announcement store ("Org..." under /api/comms/announcements) and the reader feed ("My..."); the M4a course board constants are untouched by this wave
    private static IEnumerable<FieldInfo> AnnouncementRouteConstants() =>
        typeof(Routes).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string)
            && (f.Name.StartsWith("OrgAnnouncement", StringComparison.Ordinal) || f.Name.StartsWith("MyAnnouncement", StringComparison.Ordinal)));

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
