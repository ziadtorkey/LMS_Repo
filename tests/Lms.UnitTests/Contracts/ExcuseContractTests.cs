using System.Text.Json;
using System.Text.RegularExpressions;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Auditing;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Excuses;
using Lms.Shared.Notifications;

namespace Lms.UnitTests.Contracts;

/// <summary>M5 contract, wave 3 (plan Task 3): absence excuses, the review queue, attachments and the D61 settings.</summary>
public sealed class ExcuseContractTests
{
    private static readonly JsonSerializerOptions Web = JsonSerializerOptions.Web;
    private static readonly Guid Id = Guid.Parse("0198a6f2-0000-7000-8000-00000000002a");
    private static readonly Guid Other = Guid.Parse("0198a6f2-0000-7000-8000-00000000002b");

    private static IReadOnlyList<string> ExcusesOf(string role) =>
        BuiltInRoles.Get(role).DefaultPermissions.Where(p => p.StartsWith("Excuses.", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();

    [Fact]
    public void Excuse_permissions_follow_the_matrix()
    {
        string[] declared = [Permissions.Excuses.Submit, Permissions.Excuses.Review, Permissions.Excuses.Escalate];
        Assert.Equal(["Excuses.Submit", "Excuses.Review", "Excuses.Escalate"], declared);
        Assert.All(declared, p => Assert.Contains(p, Permissions.All));

        string[] reviewers = ["Excuses.Escalate", "Excuses.Review"];
        Assert.Equal(reviewers, ExcusesOf(BuiltInRoles.OrgAdmin));
        Assert.Equal(reviewers, ExcusesOf(BuiltInRoles.Manager));
        Assert.Equal(reviewers, ExcusesOf(BuiltInRoles.Counselor));
        Assert.Empty(ExcusesOf(BuiltInRoles.Teacher)); // L16: a teacher does not review excuses
        Assert.Empty(ExcusesOf(BuiltInRoles.Supervisor));
        Assert.Equal(["Excuses.Submit"], ExcusesOf(BuiltInRoles.Student)); // effective only under GuardiansAndStudents (D61)
        Assert.Equal(["Excuses.Submit"], ExcusesOf(BuiltInRoles.Guardian));
    }

    [Fact]
    public void Submitting_an_excuse_reaches_only_the_callers_own_children_so_it_is_self_service()
    {
        Assert.Contains(Permissions.Excuses.Submit, Permissions.SelfService);
        Assert.DoesNotContain(Permissions.Excuses.Review, Permissions.SelfService);
        Assert.DoesNotContain(Permissions.Excuses.Escalate, Permissions.SelfService);
    }

    [Fact]
    public void Excuse_catalogs_match_the_plan()
    {
        Assert.Equal(["Submitted", "Approved", "Rejected", "Escalated", "Withdrawn"], ExcuseStatuses.All);
        Assert.Equal(["Submitted", "Escalated"], ExcuseStatuses.Pending);
        Assert.Equal(["Illness", "Family", "Official", "Other"], ExcuseReasons.All);
        Assert.Equal(["GuardiansOnly", "GuardiansAndStudents"], ExcuseSubmitters.All);
        Assert.Equal(["TooLate", "ReasonNotAllowed", "AttachmentRequired", "NothingToExcuse", "DatesInvalid"], ExcusePolicyViolations.All);

        foreach (var list in new[] { ExcuseStatuses.All, ExcuseReasons.All, ExcuseSubmitters.All, ExcusePolicyViolations.All })
        {
            Assert.Equal(list.Count, list.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void Excuse_attachment_limits_follow_l10()
    {
        Assert.Equal(["image/jpeg", "image/png", "image/webp", "application/pdf"], ExcuseLimits.AttachmentContentTypes);
        Assert.Equal(["image/jpeg", "image/png", "image/webp"], ExcuseLimits.InlinePreviewContentTypes); // a PDF downloads only
        Assert.All(ExcuseLimits.InlinePreviewContentTypes, t => Assert.Contains(t, ExcuseLimits.AttachmentContentTypes));
        Assert.DoesNotContain("application/pdf", ExcuseLimits.InlinePreviewContentTypes);
        Assert.Equal((5, 5, 365), (ExcuseLimits.AttachmentUrlMinutes, ExcuseLimits.MaxAttachmentMbDefault, ExcuseLimits.AttachmentRetentionDaysDefault));
        Assert.Equal((1, 500, 1000, 10), (ExcuseLimits.CommentMinLength, ExcuseLimits.CommentMaxLength, ExcuseLimits.ReasonTextMaxLength, ExcuseLimits.RecentAbsencesShown));
    }

    [Fact]
    public void Excuse_settings_match_the_plan_and_d61()
    {
        Assert.Equal("GuardiansOnly", SettingKeys.ExcusesSubmitters.Default);
        Assert.Equal(5, SettingKeys.ExcusesMaxDaysAfterAbsence.Default);
        Assert.Equal(ExcuseReasons.All, SettingKeys.ExcusesAllowedReasons.Default);
        Assert.Empty(SettingKeys.ExcusesAttachmentRequiredReasons.Default);
        Assert.Equal(5, SettingKeys.ExcusesMaxAttachmentMb.Default);
        Assert.Equal(365, SettingKeys.ExcusesAttachmentRetentionDays.Default);

        var submitters = SettingCatalog.Find("Excuses.Submitters")!;
        Assert.Equal(SettingValueKinds.Enum, submitters.Kind);
        Assert.Equal("\"GuardiansOnly\"", submitters.DefaultJson);
        Assert.Equal(ExcuseSubmitters.All, submitters.AllowedValues);
        var allowed = SettingCatalog.Find("Excuses.AllowedReasons")!;
        Assert.Equal(SettingValueKinds.StringSet, allowed.Kind);
        Assert.Equal(JsonSerializer.Serialize(ExcuseReasons.All), allowed.DefaultJson);
        Assert.Equal(ExcuseReasons.All, allowed.AllowedValues);
        Assert.Equal("[]", SettingCatalog.Find("Excuses.AttachmentRequiredReasons")!.DefaultJson);
        Assert.Equal((1, 60), (SettingCatalog.Find("Excuses.MaxDaysAfterAbsence")!.Min, SettingCatalog.Find("Excuses.MaxDaysAfterAbsence")!.Max));
        Assert.Equal((1, 25), (SettingCatalog.Find("Excuses.MaxAttachmentMb")!.Min, SettingCatalog.Find("Excuses.MaxAttachmentMb")!.Max));
    }

    [Fact]
    public void Excuse_problem_codes_exist()
    {
        Assert.Equal(
            ["excuse_out_of_policy", "excuse_submitter_not_allowed", "excuse_reviewer_is_submitter", "excuse_comment_required", "excuse_attachment_invalid", "excuse_not_pending"],
            new[]
            {
                ProblemCodes.excuse_out_of_policy, ProblemCodes.excuse_submitter_not_allowed, ProblemCodes.excuse_reviewer_is_submitter,
                ProblemCodes.excuse_comment_required, ProblemCodes.excuse_attachment_invalid, ProblemCodes.excuse_not_pending,
            });
    }

    [Fact]
    public void Excuse_entities_are_audited()
    {
        Assert.Contains("AbsenceExcuse", AuditCatalog.EntityTypes);
    }

    [Fact]
    public void Excuse_notifications_name_their_recipients_and_carry_no_free_text()
    {
        var decided = NotificationEvents.Find(NotificationEvents.ExcusesDecided)!;
        var escalated = NotificationEvents.Find(NotificationEvents.ExcusesEscalated)!;

        Assert.Equal("Excuses.Decided", decided.Key);
        Assert.Empty(decided.DefaultRecipientRoles); // the submitter
        Assert.Equal(["StudentNameAr", "StudentNameEn", "Decision"], decided.Parameters);
        Assert.Equal("Excuses.Escalated", escalated.Key);
        Assert.Empty(escalated.DefaultRecipientRoles); // the chosen reviewer
        Assert.Equal(["StudentNameAr", "StudentNameEn"], escalated.Parameters);
        // The reason text, the comment and the attachment never travel in a notification.
        Assert.DoesNotContain(decided.Parameters.Concat(escalated.Parameters), p => p.Contains("Comment", StringComparison.OrdinalIgnoreCase) || p.Contains("Reason", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Excuse_routes_match_the_plan()
    {
        Assert.Equal("/api/students/{id:guid}/excuse-policy", Routes.StudentExcusePolicyPattern);
        Assert.Equal("/api/students/{id:guid}/excusable-sessions", Routes.StudentExcusableSessionsPattern);
        Assert.Equal("/api/excuses", Routes.ExcusesPattern);
        Assert.Equal("/api/excuses/attachments", Routes.ExcuseAttachmentsPattern);
        Assert.Equal("/api/me/excuses", Routes.MeExcuses);
        Assert.Equal("/api/excuses/{id:guid}/withdraw", Routes.ExcuseWithdrawPattern);
        Assert.Equal("/api/excuses/{id:guid}/attachment-url", Routes.ExcuseAttachmentUrlPattern);
        Assert.Equal("/api/excuses/review", Routes.ExcuseReviewPattern);
        Assert.Equal("/api/excuses/{id:guid}/approve", Routes.ExcuseApprovePattern);
        Assert.Equal("/api/excuses/{id:guid}/reject", Routes.ExcuseRejectPattern);
        Assert.Equal("/api/excuses/{id:guid}/escalate", Routes.ExcuseEscalatePattern);
        Assert.Equal("/api/excuses/{id:guid}/escalation-targets", Routes.ExcuseEscalationTargetsPattern);

        Assert.Equal($"/api/students/{Id}/excuse-policy", Routes.StudentExcusePolicy(Id));
        Assert.Equal($"/api/students/{Id}/excusable-sessions?from=2027-01-01&to=2027-01-31", Routes.StudentExcusableSessions(Id, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31)));
        Assert.Equal($"/api/excuses/{Other}/approve", Routes.ExcuseApprove(Other));
        Assert.Equal($"/api/excuses/{Other}/escalation-targets", Routes.ExcuseEscalationTargets(Other));
        Assert.Equal($"/api/excuses/{Other}/attachment-url", Routes.ExcuseAttachmentUrl(Other));

        StudentContractTests.AssertUniqueApiRoutes();
    }

    [Fact]
    public void Every_excuse_route_has_a_permission_doc_comment()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lms.Shared", "Routes.Excuses.cs"));
        var matches = Regex.Matches(text, @"((?:\s*///[^\n]*\n)+)\s*public const string (\w+Pattern|MeExcuses) =");
        var routes = matches.Select(m => (Name: m.Groups[2].Value, Doc: m.Groups[1].Value)).ToList();

        Assert.True(routes.Count >= 12, $"found {routes.Count} documented route constants");
        Assert.All(routes, r => Assert.Matches(@"Excuses\.\w+", r.Doc));
        Assert.Equal(Regex.Matches(text, @"public const string \w+Pattern =").Count + 1, routes.Count); // + MeExcuses
    }

    [Fact]
    public void Excuse_dtos_round_trip()
    {
        var now = DateTime.UtcNow;
        var policy = new ExcusePolicyDto(Id, ExcuseSubmitters.GuardiansOnly, false, 5, ExcuseReasons.All, [ExcuseReasons.Illness], 5, ExcuseLimits.AttachmentContentTypes, now);
        var excusable = new ExcusableSessionDto(Id, new DateOnly(2027, 1, 5), "رياضيات", "Math", new TimeOnly(8, 0), null, AttendanceStatuses.Absent);
        var attachment = new ExcuseAttachmentDto(Id, "note.pdf", "application/pdf", 1234);
        var submit = new SubmitExcuseRequest(Id, ExcuseReasons.Illness, "fever", new DateOnly(2027, 1, 5), new DateOnly(2027, 1, 6), [Other], Id);
        var excuse = new ExcuseDto(Id, Other, "سارة", "Sara", "000123", Id, "محمد", "Mohammed", ExcuseReasons.Illness, "fever",
            new DateOnly(2027, 1, 5), new DateOnly(2027, 1, 6), ExcuseStatuses.Escalated,
            [new ExcuseSessionDto(Other, new DateOnly(2027, 1, 5), "رياضيات", "Math", AttendanceStatuses.Absent)],
            true, "note.pdf", null, null, null, null, Other, "المدير", "Manager", now, null, false,
            [new ExcuseHistoryDto(ExcuseStatuses.Submitted, Id, "محمد", "Mohammed", null, now)], now);
        var item = new ExcuseReviewItemDto(excuse, [new RecentAbsenceDto(new DateOnly(2027, 1, 4), "علوم", "Science", AttendanceStatuses.Late)], 2);
        var reject = new RejectExcuseRequest("no proof");
        var escalate = new EscalateExcuseRequest(Other, null);
        var target = new EscalationTargetDto(Other, "المدير", "Manager", "مدير", "Manager", true);
        var url = new ExcuseAttachmentUrlDto("https://files.example/x?sig=1", now.AddMinutes(5), "note.pdf", "application/pdf", false);

        Assert.False(RoundTrip(policy).CanSubmit);
        Assert.Equal(ExcuseReasons.All, RoundTrip(policy).AllowedReasons);
        Assert.Equal(AttendanceStatuses.Absent, RoundTrip(excusable).Status);
        Assert.Equal(1234, RoundTrip(attachment).SizeBytes);
        Assert.Equal(new DateOnly(2027, 1, 6), RoundTrip(submit).DateTo);
        Assert.Equal(Id, RoundTrip(submit).AttachmentFileId);
        Assert.Equal(ExcuseStatuses.Escalated, RoundTrip(excuse).Status);
        Assert.True(RoundTrip(excuse).HasAttachment);
        Assert.Single(RoundTrip(excuse).History);
        Assert.Equal(2, RoundTrip(item).AgeDays);
        Assert.Equal(AttendanceStatuses.Late, RoundTrip(item).RecentAbsences.Single().Status);
        Assert.Equal("no proof", RoundTrip(reject).Comment);
        Assert.Null(RoundTrip(escalate).Comment);
        Assert.True(RoundTrip(target).IsDefault);
        Assert.False(RoundTrip(url).InlinePreview);
        Assert.Equal((1, Paging.DefaultPageSize), (new MyExcusesQuery().Page, new MyExcusesQuery().PageSize));
        Assert.Null(new ExcuseReviewQuery().Status); // null = the pending ones
    }

    [Fact]
    public void Excuse_dtos_never_carry_the_attachment_itself_or_a_storage_key()
    {
        // The attachment is fetched through a 5-minute signed URL; no DTO exposes a stored path or bytes.
        Type[] types = [typeof(ExcuseDto), typeof(ExcuseReviewItemDto), typeof(ExcuseAttachmentDto), typeof(ExcusePolicyDto)];
        string[] forbidden = ["Path", "StorageKey", "Bytes", "Content"];

        Assert.All(types, t => Assert.DoesNotContain(t.GetProperties(), p => forbidden.Any(f => p.Name.Equals(f, StringComparison.OrdinalIgnoreCase))));
        Assert.Contains(typeof(ExcuseDto).GetProperties(), p => p.Name == "HasAttachment" && p.PropertyType == typeof(bool));
    }

    [Fact]
    public void Every_timed_excuse_dto_carries_the_server_clock()
    {
        Type[] timed = [typeof(ExcusePolicyDto), typeof(ExcuseDto)];

        Assert.All(timed, t => Assert.Contains(t.GetProperties(), p => p.Name == "ServerNowUtc" && p.PropertyType == typeof(DateTime)));
    }

    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Web), Web)!;

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
