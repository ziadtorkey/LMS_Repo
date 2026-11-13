using System.Text.Json;
using Lms.Shared;
using Lms.Shared.Auditing;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;
using Lms.Shared.Notifications;

namespace Lms.UnitTests.Contracts;

/// <summary>M4b contract, wave 3a (plan Task 1): live-session permissions, catalogs, DTOs, routes, codes and events.</summary>
public sealed class LiveSessionContractTests
{
    private static readonly JsonSerializerOptions Web = JsonSerializerOptions.Web;
    private static readonly Guid Id = Guid.Parse("0198a6f2-0000-7000-8000-00000000001a");
    private static readonly Guid Other = Guid.Parse("0198a6f2-0000-7000-8000-00000000001b");

    [Fact]
    public void Live_session_catalogs_match_the_plan()
    {
        Assert.Equal(["Scheduled", "Live", "Ended", "Cancelled", "SyncFailed"], LiveSessionStatuses.All);
        Assert.Equal(["Provider", "Manual"], RecordingSources.All);
        Assert.Equal(["Matched", "Guest"], ParticipantMatchStates.All);
        Assert.Equal(["This", "ThisAndFollowing"], SeriesEditScopes.All);
        Assert.Equal(Enum.GetNames<DayOfWeek>(), RecurrenceDays.All);

        foreach (var list in new[]
                 {
                     LiveSessionStatuses.All, RecordingSources.All, ParticipantMatchStates.All, SeriesEditScopes.All,
                     RecurrenceDays.All,
                 })
        {
            Assert.Equal(list.Count, list.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void Live_session_limits_match_the_plan()
    {
        Assert.Equal((1, 200), (LiveSessionLimits.TitleMinLength, LiveSessionLimits.TitleMaxLength));
        Assert.Equal((15, 480), (LiveSessionLimits.DurationMinMinutes, LiveSessionLimits.DurationMaxMinutes));
        Assert.Equal(52, LiveSessionLimits.MaxOccurrences);
        Assert.Equal(5, LiveSessionLimits.UpcomingMax);
        Assert.Equal(("Live:MaxHostSharePercent", 25, 1, 100),
            (LiveSessionLimits.MaxHostSharePercentConfigKey, LiveSessionLimits.MaxHostSharePercentDefault,
             LiveSessionLimits.MaxHostSharePercentMin, LiveSessionLimits.MaxHostSharePercentMax));
    }

    [Fact]
    public void Live_session_modules_complete_on_none_manual_or_view()
    {
        Assert.Equal(["None", "Manual", "View"], CompletionRules.ForActivity(ModuleKinds.LiveSession));
        Assert.All(CompletionRules.ForActivity(ModuleKinds.LiveSession), r => Assert.Contains(r, CompletionRules.All));
    }

    [Fact]
    public void Live_session_permissions_and_relationships_match_the_plan()
    {
        Assert.Equal(("LiveSessions.View", "LiveSessions.Manage"), (Permissions.LiveSessions.View, Permissions.LiveSessions.Manage));
        Assert.DoesNotContain(Permissions.LiveSessions.View, Permissions.SelfService);

        Assert.Equal(Relationships.OwnClassCourses, RelationshipRules.For(BuiltInRoles.Teacher, Permissions.LiveSessions.Manage));
        Assert.Equal(Relationships.OwnEnrollments, RelationshipRules.For(BuiltInRoles.Student, Permissions.LiveSessions.View));
        Assert.Null(RelationshipRules.For(BuiltInRoles.Student, Permissions.LiveSessions.Manage));

        string[] none = [BuiltInRoles.Counselor, BuiltInRoles.Guardian];
        Assert.All(none, r => Assert.DoesNotContain(BuiltInRoles.Get(r).DefaultPermissions,
            p => p.StartsWith("LiveSessions.", StringComparison.Ordinal)));
        Assert.DoesNotContain(Permissions.LiveSessions.Manage, BuiltInRoles.Get(BuiltInRoles.Supervisor).DefaultPermissions);
        Assert.DoesNotContain(Permissions.LiveSessions.Manage, BuiltInRoles.Get(BuiltInRoles.Student).DefaultPermissions);
    }

    [Fact]
    public void Live_session_routes_match_the_plan()
    {
        Assert.Equal("/api/class-courses/{id:guid}/live-sessions", Routes.ClassCourseLiveSessionsPattern);
        Assert.Equal("/api/live-sessions/{id:guid}", Routes.LiveSessionPattern);
        Assert.Equal("/api/live-sessions/{id:guid}/join", Routes.LiveSessionJoinPattern);
        Assert.Equal("/api/live-sessions/{id:guid}/recording", Routes.LiveSessionRecordingPattern);
        Assert.Equal("/api/live-sessions/{id:guid}/participants", Routes.LiveSessionParticipantsPattern);
        Assert.Equal("/api/live-sessions/upcoming", Routes.LiveSessionsUpcoming);
        Assert.Equal("/api/live-sessions/provider-status", Routes.LiveSessionsProviderStatus);
        Assert.Equal("/api/integrations/zoom/webhook", Routes.ZoomWebhook);
        Assert.Equal("/api/dev/zoom/events", Routes.DevZoomEvents);

        Assert.Equal($"/api/class-courses/{Id}/live-sessions", Routes.ClassCourseLiveSessions(Id));
        Assert.Equal($"/api/live-sessions/{Id}", Routes.LiveSession(Id));
        Assert.Equal($"/api/live-sessions/{Id}?scope=ThisAndFollowing", Routes.LiveSession(Id, SeriesEditScopes.ThisAndFollowing));
        Assert.Equal($"/api/live-sessions/{Id}/join", Routes.LiveSessionJoin(Id));
        Assert.Equal($"/api/live-sessions/{Id}/recording", Routes.LiveSessionRecording(Id));
        Assert.Equal($"/api/live-sessions/{Id}/participants", Routes.LiveSessionParticipants(Id));

        StudentContractTests.AssertUniqueApiRoutes();
    }

    [Fact]
    public void Live_session_problem_codes_exist()
    {
        Assert.Equal(
            ["provider_unavailable", "provider_not_configured", "live_session_not_open", "feature_disabled", "recurrence_invalid"],
            new[]
            {
                ProblemCodes.provider_unavailable, ProblemCodes.provider_not_configured, ProblemCodes.live_session_not_open,
                ProblemCodes.feature_disabled, ProblemCodes.recurrence_invalid,
            });
    }

    [Fact]
    public void Start_weekday_outside_days_of_week_is_refused_with_recurrence_invalid()
    {
        // Contract rule (LiveRecurrenceInput docs): the local weekday of StartsAtUtc in TimeZoneId must be in DaysOfWeek,
        // else 400 recurrence_invalid. 2026-11-22 06:00Z is a Sunday in Asia/Riyadh; the recurrence names only Tuesday.
        var start = new DateTime(2026, 11, 22, 6, 0, 0, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(start, TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh"));
        var recurrence = new LiveRecurrenceInput(1, ["Tuesday"], null, 4, "Asia/Riyadh");

        Assert.DoesNotContain(local.DayOfWeek.ToString(), recurrence.DaysOfWeek);
        Assert.Equal("recurrence_invalid", ProblemCodes.recurrence_invalid);
        Assert.Contains(local.DayOfWeek.ToString(), RecurrenceDays.All);
    }

    [Fact]
    public void Live_reminder_names_its_recipients_and_carries_bilingual_titles()
    {
        var reminder = NotificationEvents.Find(NotificationEvents.LiveReminder)!;

        Assert.Equal("Live.Reminder", reminder.Key);
        Assert.Empty(reminder.DefaultRecipientRoles);
        Assert.Equal(["TitleAr", "TitleEn", "CourseNameAr", "CourseNameEn", "StartsAtUtc"], reminder.Parameters);
    }

    [Fact]
    public void Series_and_sessions_are_audited_but_participants_are_not()
    {
        Assert.Contains("LiveSessionSeries", AuditCatalog.EntityTypes);
        Assert.Contains("LiveSession", AuditCatalog.EntityTypes);
        Assert.DoesNotContain("LiveSessionParticipant", AuditCatalog.EntityTypes);
        Assert.DoesNotContain("ZoomWebhookReceipt", AuditCatalog.EntityTypes);
    }

    [Fact]
    public void Live_session_dtos_round_trip()
    {
        var settings = new ModuleSettingsInput("حصة مباشرة", "Live class", ModuleVisibilities.Published, null, null,
            ModuleTargets.ClassCourse, null, CompletionRules.View, null);
        var recurrence = new LiveRecurrenceInput(1, ["Sunday", "Tuesday"], null, 10, "Asia/Riyadh");
        var create = new CreateLiveSessionRequest(Id, settings, new DateTime(2026, 11, 22, 6, 0, 0, DateTimeKind.Utc), 45, recurrence);
        var update = new UpdateLiveSessionRequest(settings, create.StartsAtUtc, 60, SeriesEditScopes.ThisAndFollowing);
        var module = new ModuleNodeDto(Guid.NewGuid(), ModuleKinds.LiveSession, 1, "حصة", "Class", ModuleVisibilities.Published,
            null, null, ModuleTargets.ClassCourse, null, null, null, CompletionRules.View, true, null);
        var session = new LiveSessionDto(Id, Other, Id, Other, 2, 10, module, create.StartsAtUtc, create.StartsAtUtc.AddMinutes(45),
            45, LiveSessionStatuses.Scheduled, new LiveRecurrenceDto(1, ["Sunday"], null, 10, "Asia/Riyadh"),
            "https://zoom.us/rec/share/x", RecordingSources.Manual, create.StartsAtUtc.AddMinutes(-15), true, DateTime.UtcNow);
        var join = new LiveSessionJoinDto("https://zoom.us/j/1", create.StartsAtUtc.AddMinutes(45), DateTime.UtcNow);
        var participant = new LiveParticipantDto(Id, "Sara", ParticipantMatchStates.Matched, Other, "سارة", "Sara",
            DateTime.UtcNow, null, 40);
        var upcoming = new UpcomingLiveSessionDto(Id, Other, Id, "رياضيات", "Math", "حصة", "Class", create.StartsAtUtc,
            create.StartsAtUtc.AddMinutes(45), LiveSessionStatuses.Scheduled, create.StartsAtUtc.AddMinutes(-15), DateTime.UtcNow);

        Assert.Equal(10, RoundTrip(create).Recurrence!.Count);
        Assert.Equal(["Sunday", "Tuesday"], RoundTrip(create).Recurrence!.DaysOfWeek);
        Assert.Null(new CreateLiveSessionRequest(Id, settings, create.StartsAtUtc, 45).Recurrence);
        Assert.Equal(SeriesEditScopes.ThisAndFollowing, RoundTrip(update).Scope);
        Assert.Equal(SeriesEditScopes.This, new UpdateLiveSessionRequest(settings, create.StartsAtUtc, 45).Scope);
        Assert.Equal(module, RoundTrip(session).Module);
        Assert.Equal("Asia/Riyadh", RoundTrip(session).Recurrence!.TimeZoneId);
        Assert.Equal("https://zoom.us/j/1", RoundTrip(join).Url);
        Assert.Equal(Other, RoundTrip(participant).StudentId);
        Assert.Equal("Math", RoundTrip(upcoming).CourseNameEn);
        Assert.True(RoundTrip(new ProviderStatusDto(true, false)).Configured);
        Assert.Equal("https://example.org/r", RoundTrip(new RecordingLinkRequest("https://example.org/r")).Url);
        Assert.Equal((1, Paging.DefaultPageSize), (new LiveSessionQuery().Page, new LiveSessionQuery().PageSize));
        Assert.Null(new LiveParticipantQuery().MatchState);
        Assert.False(RoundTrip(new LiveSessionListItemDto(Id, Other, null, null, "حصة", "Class", create.StartsAtUtc,
            create.StartsAtUtc.AddMinutes(45), LiveSessionStatuses.SyncFailed, false, DateTime.UtcNow)).HasRecording);
    }

    [Fact]
    public void Only_the_join_dto_carries_a_join_or_start_url()
    {
        Type[] noJoinUrl =
        [
            typeof(LiveSessionDto), typeof(LiveSessionListItemDto), typeof(UpcomingLiveSessionDto), typeof(LiveParticipantDto),
            typeof(ProviderStatusDto), typeof(CreateLiveSessionRequest), typeof(UpdateLiveSessionRequest),
        ];

        Assert.All(noJoinUrl, t => Assert.DoesNotContain(t.GetProperties(),
            p => p.Name.Contains("Join", StringComparison.Ordinal) && p.Name.EndsWith("Url", StringComparison.Ordinal)
                || p.Name.Contains("Start", StringComparison.Ordinal) && p.Name.EndsWith("Url", StringComparison.Ordinal)
                || p.Name == "Url" || p.Name.Contains("HostUser", StringComparison.Ordinal)));
        Assert.Equal(["Url", "ExpiresAtUtc", "ServerNowUtc"], typeof(LiveSessionJoinDto).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain(typeof(LiveParticipantDto).GetProperties(), p => p.Name.Contains("Email", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_timed_live_session_dto_carries_the_server_clock()
    {
        Type[] timed = [typeof(LiveSessionDto), typeof(LiveSessionListItemDto), typeof(LiveSessionJoinDto), typeof(UpcomingLiveSessionDto)];

        Assert.All(timed, t => Assert.Contains(t.GetProperties(), p => p.Name == "ServerNowUtc" && p.PropertyType == typeof(DateTime)));
    }

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Web), Web)!;
}
