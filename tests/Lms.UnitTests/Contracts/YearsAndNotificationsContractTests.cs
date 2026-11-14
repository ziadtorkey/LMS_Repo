using System.Text.Json;
using System.Text.RegularExpressions;
using Lms.Shared;
using Lms.Shared.AcademicYears;
using Lms.Shared.Authorization;
using Lms.Shared.Notifications;

namespace Lms.UnitTests.Contracts;

public partial class YearsAndNotificationsContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("^[A-Z][A-Za-z]+\\.[A-Z][A-Za-z]+$")]
    private static partial Regex ModuleEventFormat();

    [Fact]
    public void Notification_event_keys_are_unique_and_Module_Event_shaped()
    {
        var keys = NotificationEvents.All.Select(e => e.Key).ToList();

        Assert.Equal(
            ["Access.RoleAssigned", "Access.RoleRemoved", "Organization.PolicyChanged",
             "Staff.ExpiringSoon", "Staff.Expired", "Imports.Completed",
             "Students.ChangeRequested", "Students.ChangeRequestDecided", "Teaching.Assigned", "Blueprints.Pushed",
             "Enrollment.ExceptionRequested", "Enrollment.ExceptionDecided", "Enrollment.StudentTransferred",
             "Assignments.Published", "Assignments.DueSoon", "Assignments.Graded", "Quizzes.Opened",
             "Forums.Announcement", "Live.Reminder", "Grades.Published",
             "Messages.NewMessage", "Announcements.Published", "Messaging.Warning", "Messaging.Restricted", "Messaging.ReportResolved", "Moderation.ReportReceived", "Moderation.SafetyReportReceived", "Comms.CapReached",
             "Attendance.Absent", "Excuses.Decided", "Excuses.Escalated", "Attendance.ThresholdCrossed"], keys);
        Assert.All(keys, k => Assert.Matches(ModuleEventFormat(), k));
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("Access.RoleAssigned", NotificationEvents.AccessRoleAssigned);
        Assert.Equal("Access.RoleRemoved", NotificationEvents.AccessRoleRemoved);
        Assert.Equal("Organization.PolicyChanged", NotificationEvents.OrganizationPolicyChanged);
    }

    [Fact]
    public void Default_recipient_roles_are_built_in_keys()
    {
        var builtInKeys = BuiltInRoles.All.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);

        Assert.All(NotificationEvents.All, e => Assert.All(e.DefaultRecipientRoles, r => Assert.Contains(r, builtInKeys)));
        Assert.Equal([BuiltInRoles.OrgAdmin, BuiltInRoles.Manager],
            NotificationEvents.Find(NotificationEvents.OrganizationPolicyChanged)!.DefaultRecipientRoles);
        Assert.Empty(NotificationEvents.Find(NotificationEvents.AccessRoleAssigned)!.DefaultRecipientRoles);
        Assert.Empty(NotificationEvents.Find(NotificationEvents.AccessRoleRemoved)!.DefaultRecipientRoles);
    }

    [Fact]
    public void Event_parameters_match_plan()
    {
        string[] roleParameters = ["RoleNameAr", "RoleNameEn", "OrgUnitNameAr", "OrgUnitNameEn"];

        Assert.Equal(roleParameters, NotificationEvents.Find(NotificationEvents.AccessRoleAssigned)!.Parameters);
        Assert.Equal(roleParameters, NotificationEvents.Find(NotificationEvents.AccessRoleRemoved)!.Parameters);
        Assert.Equal(["PolicyKey", "OrgUnitNameAr", "OrgUnitNameEn", "State"],
            NotificationEvents.Find(NotificationEvents.OrganizationPolicyChanged)!.Parameters);
        Assert.Null(NotificationEvents.Find("Nope.Nope"));
    }

    [Fact]
    public void M4a_events_name_their_recipients_and_carry_bilingual_titles()
    {
        string[] activity = ["TitleAr", "TitleEn", "CourseNameAr", "CourseNameEn"];

        Assert.Equal(activity, NotificationEvents.Find(NotificationEvents.AssignmentsPublished)!.Parameters);
        Assert.Equal([.. activity, "DueAt"], NotificationEvents.Find(NotificationEvents.AssignmentsDueSoon)!.Parameters);
        Assert.Equal(activity, NotificationEvents.Find(NotificationEvents.AssignmentsGraded)!.Parameters);
        Assert.Equal(activity, NotificationEvents.Find(NotificationEvents.QuizzesOpened)!.Parameters);
        Assert.Equal(["Title", "CourseNameAr", "CourseNameEn"],
            NotificationEvents.Find(NotificationEvents.ForumsAnnouncement)!.Parameters);
        Assert.All(
            new[]
            {
                NotificationEvents.AssignmentsPublished, NotificationEvents.AssignmentsDueSoon,
                NotificationEvents.AssignmentsGraded, NotificationEvents.QuizzesOpened, NotificationEvents.ForumsAnnouncement,
            },
            k => Assert.Empty(NotificationEvents.Find(k)!.DefaultRecipientRoles));
    }

    [Fact]
    public void Constants_match_plan()
    {
        Assert.Equal(("Planned", "Active", "Closed"),
            (AcademicYearStatuses.Planned, AcademicYearStatuses.Active, AcademicYearStatuses.Closed));
        Assert.Equal(("Holiday", "Event"), (CalendarEventKinds.Holiday, CalendarEventKinds.Event));
        Assert.Equal(["InApp", "Email", "Push"], NotificationChannels.All);
        Assert.Equal(("InApp", "Email", "Push"),
            (NotificationChannels.InApp, NotificationChannels.Email, NotificationChannels.Push));
        Assert.Equal("/hubs/notifications", NotificationHub.Path);
        Assert.Equal("notification", NotificationHub.ReceiveMethod);
    }

    [Fact]
    public void Years_and_notification_routes_match_plan()
    {
        var id = Guid.Parse("0198a6f2-0000-7000-8000-000000000004");

        Assert.Equal("/api/academic-years", Routes.AcademicYears);
        Assert.Equal("/api/academic-years/effective", Routes.AcademicYearsEffective);
        Assert.Equal("/api/terms", Routes.Terms);
        Assert.Equal("/api/calendar-events", Routes.CalendarEvents);
        Assert.Equal("/api/me/notifications", Routes.MyNotifications);
        Assert.Equal("/api/me/notifications/read-all", Routes.MyNotificationsReadAll);
        Assert.Equal("/api/me/notification-preferences", Routes.MyNotificationPreferences);

        Assert.Equal("/api/academic-years/{id:guid}", Routes.AcademicYearPattern);
        Assert.Equal("/api/academic-years/{id:guid}/activate", Routes.AcademicYearActivatePattern);
        Assert.Equal("/api/academic-years/{id:guid}/terms", Routes.AcademicYearTermsPattern);
        Assert.Equal("/api/academic-years/{id:guid}/calendar", Routes.AcademicYearCalendarPattern);
        Assert.Equal("/api/terms/{id:guid}", Routes.TermPattern);
        Assert.Equal("/api/calendar-events/{id:guid}", Routes.CalendarEventPattern);
        Assert.Equal("/api/me/notifications/{id:guid}/read", Routes.MyNotificationReadPattern);

        Assert.Equal($"/api/academic-years/{id}", Routes.AcademicYear(id));
        Assert.Equal($"/api/academic-years/{id}/activate", Routes.AcademicYearActivate(id));
        Assert.Equal($"/api/academic-years/{id}/terms", Routes.AcademicYearTerms(id));
        Assert.Equal($"/api/academic-years/{id}/calendar", Routes.AcademicYearCalendar(id));
        Assert.Equal($"/api/terms/{id}", Routes.Term(id));
        Assert.Equal($"/api/calendar-events/{id}", Routes.CalendarEvent(id));
        Assert.Equal($"/api/me/notifications/{id}/read", Routes.MyNotificationRead(id));
    }

    [Fact]
    public void Year_context_round_trips_with_terms_dates_and_arabic()
    {
        var term = new TermDto(Guid.NewGuid(), "الفصل الأول", "Term 1", 1, new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 15), true);
        var year = new AcademicYearDto(Guid.NewGuid(), Guid.NewGuid(), "الجذر", "Root", "2026/2027", "2026/2027",
            new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 30), AcademicYearStatuses.Active, true, [term]);
        var context = new YearContextDto([year], year.Id, year.OrgUnitId);

        var back = RoundTrip(context);

        Assert.Equal(context.CurrentYearId, back.CurrentYearId);
        var yearBack = Assert.Single(back.Years);
        Assert.Equal((year.StartDate, year.EndDate, year.IsInherited), (yearBack.StartDate, yearBack.EndDate, yearBack.IsInherited));
        Assert.Equal(term, Assert.Single(yearBack.Terms));
    }

    [Fact]
    public void Requests_and_calendar_round_trip()
    {
        var saveYear = new SaveAcademicYearRequest(Guid.NewGuid(), "٢٠٢٦", "2026", new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 30));
        var saveTerm = new SaveTermRequest("الفصل الثاني", "Term 2", new DateOnly(2027, 2, 1), new DateOnly(2027, 6, 30));
        var evt = new CalendarEventDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "الروضة", "KG", "اليوم الوطني",
            "National Day", CalendarEventKinds.Holiday, new DateOnly(2026, 9, 23), new DateOnly(2026, 9, 23), false);
        var saveEvent = new SaveCalendarEventRequest(evt.AcademicYearId, evt.OrgUnitId, evt.TitleAr, evt.TitleEn, evt.Kind,
            evt.StartDate, evt.EndDate);

        Assert.Equal(saveYear, RoundTrip(saveYear));
        Assert.Equal(saveTerm, RoundTrip(saveTerm));
        Assert.Equal(evt, RoundTrip(evt));
        Assert.Equal(saveEvent, RoundTrip(saveEvent));
    }

    [Fact]
    public void Notification_dtos_round_trip()
    {
        var notification = new NotificationDto(Guid.NewGuid(), NotificationEvents.AccessRoleAssigned,
            new Dictionary<string, string> { ["RoleNameAr"] = "المعلم", ["RoleNameEn"] = "Teacher" }, "/org",
            new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc), false);
        var page = new NotificationPageDto([notification], 1, 1);
        var preference = new NotificationPreferenceDto(NotificationEvents.AccessRoleAssigned, NotificationChannels.InApp, true, true);

        var back = RoundTrip(page);

        var item = Assert.Single(back.Items);
        Assert.Equal("المعلم", item.Parameters["RoleNameAr"]);
        Assert.Equal((1, 1), (back.Total, back.UnreadCount));
        Assert.Equal(preference, RoundTrip(preference));
        Assert.Equal(preference, Assert.Single(RoundTrip(new SetNotificationPreferencesRequest([preference])).Preferences));
    }

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Web), Web)!;
}
