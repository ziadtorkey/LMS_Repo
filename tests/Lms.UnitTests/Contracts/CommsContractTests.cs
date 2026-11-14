using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lms.Infrastructure.Access;
using Lms.Shared;
using Lms.Shared.Auditing;
using Lms.Shared.Configuration;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;

namespace Lms.UnitTests.Contracts;

/// <summary>M6 wave 2 (plan Task 2): channels, preferences, push, caps and delivery. Adds no permission; no SMS anywhere (D76).</summary>
public sealed class CommsContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Defaults_version_is_unchanged_by_wave_2()
    {
        Assert.Equal(11, BuiltInRoleDefaults.CurrentVersion); // wave 2 adds no permission
    }

    [Fact]
    public void Mandatory_events_match_the_D66_list()
    {
        var mandatory = NotificationEvents.All.Where(e => e.Mandatory).Select(e => e.Key).Order().ToList();

        Assert.Equal(
            new[] { NotificationEvents.AttendanceAbsent, NotificationEvents.MessagingReportResolved, NotificationEvents.MessagingRestricted, NotificationEvents.MessagingWarning }.Order(),
            mandatory);
        // absence notices to guardians are mandatory on in-app, email and push
        Assert.Equal(
            [NotificationChannels.InApp, NotificationChannels.Email, NotificationChannels.Push],
            NotificationEvents.Find(NotificationEvents.AttendanceAbsent)!.DefaultChannels);
    }

    [Fact]
    public void No_event_lists_sms()
    {
        var channels = NotificationEvents.All.SelectMany(e => e.DefaultChannels ?? []).Distinct().ToList();

        Assert.All(channels, c => Assert.Contains(c, NotificationChannels.All));
        Assert.DoesNotContain(channels, c => c.Contains("sms", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void No_sms_setting_route_or_dto_field_exists()
    {
        var shared = typeof(CommsSettingsDto).Assembly;
        var names = shared.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith("Lms.Shared", StringComparison.Ordinal))
            .SelectMany(t => new[] { t.Name }.Concat(t.GetProperties().Select(p => p.Name)).Concat(t.GetFields().Select(f => f.Name)))
            .ToList();

        Assert.DoesNotContain(names, n => n.Contains("Sms", StringComparison.OrdinalIgnoreCase) && !n.Contains("Smsg", StringComparison.OrdinalIgnoreCase));
        var routeValues = typeof(Routes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);
        Assert.DoesNotContain(routeValues, v => v.Contains("sms", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(SettingCatalog.All, d => d.Name.Contains("Sms", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Wave_2_catalogs_hold_the_values_of_the_plan()
    {
        Assert.Equal(["Pending", "Sending", "Sent", "Failed", "Suppressed", "Held", "Skipped"], DeliveryStatuses.All);
        Assert.Equal(["expired_while_held", "recipient_cap", "no_contact", "channel_off"], DeliverySkipReasons.All);
        Assert.Equal(["Active", "NeedsSetup", "Blocked", "Bounced", "Off"], ChannelStates.All);
        Assert.Equal(["HardBounce", "Complaint", "UserOptOut"], SuppressionReasons.All);
        Assert.Equal(3, CommsLimits.TestSendsPerDay);
        Assert.Equal(CommsLimits.TestSendsPerDay, MessagingLimits.EmailTestsPerDay);
    }

    [Fact]
    public void Comms_settings_default_to_the_plan_and_stay_client_level()
    {
        Assert.Equal((true, true), (SettingKeys.CommsEmailEnabled.Default, SettingKeys.CommsPushEnabled.Default));
        Assert.Equal((5000, 20000, 20, 50), (SettingKeys.CommsMaxEmailPerDay.Default, SettingKeys.CommsMaxPushPerDay.Default,
            SettingKeys.CommsMaxEmailPerUserPerDay.Default, SettingKeys.CommsMaxPushPerUserPerDay.Default));
        Assert.Equal((false, "21:00", "07:00", 90), (SettingKeys.CommsQuietHoursEnabled.Default, SettingKeys.CommsQuietHoursStart.Default,
            SettingKeys.CommsQuietHoursEnd.Default, SettingKeys.CommsDeliveryRetentionDays.Default));
        var catalog = SettingCatalog.All.Where(d => d.Name.StartsWith("Comms.", StringComparison.Ordinal)).ToList();
        Assert.Equal(10, catalog.Count);
        Assert.All(catalog, d => Assert.True(d.ClientLevelOnly, d.Name));
        // the catalog ranges are the platform ceilings the DTO reports
        Assert.Equal(100_000, SettingCatalog.All.Single(d => d.Name == SettingKeys.CommsMaxEmailPerDay.Name).Max);
        Assert.Equal(200_000, SettingCatalog.All.Single(d => d.Name == SettingKeys.CommsMaxPushPerDay.Name).Max);
    }

    [Fact]
    public void Wave_2_problem_codes_and_audit_entries_exist()
    {
        Assert.Equal("channel_not_allowed", ProblemCodes.channel_not_allowed);
        Assert.Equal("comms_cap_above_ceiling", ProblemCodes.comms_cap_above_ceiling);
        Assert.Equal("notification_mandatory", ProblemCodes.notification_mandatory);
        Assert.Equal("rate_limited", ProblemCodes.rate_limited);
        Assert.Contains("ChannelConsent", AuditCatalog.EntityTypes);
        Assert.Contains("CommsSettings", AuditCatalog.EntityTypes);
        Assert.DoesNotContain("UserBlock", AuditCatalog.EntityTypes); // blocks are the owner's and are not audited
    }

    [Fact]
    public void Wave_2_dtos_round_trip_with_arabic_text_and_keep_wave_1_callers_compiling()
    {
        var channel = new ContactChannelDto("Email", ChannelStates.Bounced, "m***@example.com", false, "smtp_5xx", new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc));
        var legacy = new ContactChannelDto("Push", ChannelStates.Active, null, true); // the five-argument form still compiles
        var cell = new EffectivePreferenceDto("Grades.Published", "Push", false, PreferenceSources.Default, false, DeliverySkipReasons.ChannelOff);
        var summary = new DeliverySummaryDto(new DateOnly(2026, 11, 20), [new DeliveryCountDto("Email", DeliveryStatuses.Sent, 42)], new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc));
        var settingsJson = JsonSerializer.Serialize(new CommsCeilingsDto(100_000, 200_000), Web);

        Assert.Equal(channel, Round(channel));
        Assert.Null(legacy.ConsentAtUtc);
        Assert.Equal(cell, Round(cell));
        Assert.Equal(summary.ClientDay, Round(summary).ClientDay);
        Assert.Equal("42", Round(summary).Counts.Single().Count.ToString());
        Assert.Equal("v1-الموافقة", Round(new ChannelConsentRequest("v1-الموافقة")).TextVersion);
        Assert.Equal("BKey", Round(new VapidPublicKeyDto("BKey")).PublicKey);
        Assert.Contains("maxEmailPerDay", settingsJson);
    }

    [Fact]
    public void The_new_comms_routes_document_who_may_call_them_and_are_unique()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lms.Shared", "Routes.Messaging.cs"));
        foreach (var name in new[] { "MyChannelConsentPattern", "MyVapidPublicKey", "CommsDeliverySummary", "CommsSettings", "CommsProviderStatus", "EmailEventsWebhook" })
        {
            var doc = Regex.Match(text, @"((?:\s*///[^\n]*\n)+)\s*public const string " + name + " =");
            Assert.True(doc.Success, name);
            Assert.Matches(@"Comms\.Manage|authenticated|anonymous", doc.Groups[1].Value);
        }

        Assert.Equal("/api/me/channels/email/consent", Routes.MyChannelConsent("email"));
        Assert.Equal(Routes.MyChannelConsentPattern.Replace("{channel}", "push"), Routes.MyChannelConsent("push"));
        var values = typeof(Routes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
    }

    /// <summary>L9a: a template shown in a push (or any short notice) never carries a value that could be a grade, a score or message text.</summary>
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Push_templates_have_no_value_placeholders(string culture)
    {
        var path = Path.Combine(RepoRoot(), "src", "Lms.Client", "Resources", $"SharedResource.{culture}.resx");
        var resources = XDocument.Load(path).Descendants("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Score", "Points", "Grade", "Percent", "Body", "Text", "Message", "Value", "Result" };

        foreach (var e in NotificationEvents.All.Where(e => e.DefaultChannels?.Contains(NotificationChannels.Push) == true))
        {
            Assert.True(resources.TryGetValue("Notif_" + e.Key.Replace('.', '_'), out var template), e.Key);
            var names = Regex.Matches(template!, @"\{(\w+)\}").Select(m => m.Groups[1].Value);
            Assert.DoesNotContain(names, forbidden.Contains);
            Assert.DoesNotContain(e.Parameters, forbidden.Contains);
        }
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
