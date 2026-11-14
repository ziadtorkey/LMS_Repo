using System.Text.Json;
using System.Text.RegularExpressions;
using Lms.Infrastructure.Access;
using Lms.Shared;
using Lms.Shared.Auditing;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Timetable;

namespace Lms.UnitTests.Contracts;

/// <summary>M5 contract, wave 2 (plan Task 2): periods, working days, timetable slots, conflicts, publishing, exceptions and substitutes. D60, D64.</summary>
public sealed class TimetableContractTests
{
    private static readonly JsonSerializerOptions Web = JsonSerializerOptions.Web;
    private static readonly Guid Id = Guid.Parse("0198a6f2-0000-7000-8000-00000000003a");
    private static readonly Guid Other = Guid.Parse("0198a6f2-0000-7000-8000-00000000003b");

    private static readonly string[] TimetablePermissions = ["Timetable.View", "Timetable.Manage", "Timetable.Configure", "Timetable.ViewOwn"];

    private static IReadOnlyList<string> TimetableOf(string role) =>
        BuiltInRoles.Get(role).DefaultPermissions.Where(p => p.StartsWith("Timetable.", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();

    [Fact]
    public void Timetable_permissions_follow_the_matrix()
    {
        string[] declared = [Permissions.Timetable.View, Permissions.Timetable.Manage, Permissions.Timetable.Configure, Permissions.Timetable.ViewOwn];
        Assert.Equal(TimetablePermissions, declared);
        Assert.All(TimetablePermissions, p => Assert.Contains(p, Permissions.All));

        // OrgAdmin and Manager build and configure; they hold no ViewOwn (no timetable of their own).
        string[] build = ["Timetable.Configure", "Timetable.Manage", "Timetable.View"];
        Assert.Equal(build, TimetableOf(BuiltInRoles.OrgAdmin));
        Assert.Equal(build, TimetableOf(BuiltInRoles.Manager));
        Assert.Equal(["Timetable.View"], TimetableOf(BuiltInRoles.Supervisor));
        Assert.Equal(["Timetable.View"], TimetableOf(BuiltInRoles.Counselor));
        Assert.Equal(["Timetable.View", "Timetable.ViewOwn"], TimetableOf(BuiltInRoles.Teacher));
        Assert.Equal(["Timetable.ViewOwn"], TimetableOf(BuiltInRoles.Student));
        Assert.Equal(["Timetable.ViewOwn"], TimetableOf(BuiltInRoles.Guardian));
        Assert.DoesNotContain(Permissions.Timetable.Manage, TimetableOf(BuiltInRoles.Teacher));
        Assert.DoesNotContain(Permissions.Timetable.Configure, TimetableOf(BuiltInRoles.Teacher));
    }

    [Fact]
    public void A_supervisor_reads_the_timetable_of_supervised_courses_only()
    {
        Assert.Equal(Relationships.SupervisedCourses, RelationshipRules.For(BuiltInRoles.Supervisor, Permissions.Timetable.View));
        Assert.Null(RelationshipRules.For(BuiltInRoles.Manager, Permissions.Timetable.Manage)); // unit scope
        Assert.Null(RelationshipRules.For(BuiltInRoles.Teacher, Permissions.Timetable.View)); // the unit's published timetable
    }

    [Fact]
    public void Viewing_the_own_timetable_is_self_service_for_the_escalation_guards()
    {
        Assert.Contains(Permissions.Timetable.ViewOwn, Permissions.SelfService);
        Assert.DoesNotContain(Permissions.Timetable.Manage, Permissions.SelfService);
    }

    [Fact]
    public void A_substitute_is_the_teacher_of_record_for_that_date_only()
    {
        var today = new DateOnly(2027, 2, 3);
        Assert.True(SubstituteRule.IsSubstituteOn(ExceptionKinds.Substitute, Id, Id, today, today));
        Assert.False(SubstituteRule.IsSubstituteOn(ExceptionKinds.Substitute, Id, Other, today, today)); // another user
        Assert.False(SubstituteRule.IsSubstituteOn(ExceptionKinds.Substitute, Id, Id, today.AddDays(7), today)); // another date
        Assert.False(SubstituteRule.IsSubstituteOn(ExceptionKinds.Substitute, null, Id, today, today)); // no substitute named
        Assert.False(SubstituteRule.IsSubstituteOn(ExceptionKinds.Cancelled, Id, Id, today, today)); // not a substitution
        Assert.False(SubstituteRule.IsSubstituteOn(ExceptionKinds.Moved, Id, Id, today, today));
    }

    [Fact]
    public void Timetable_defaults_were_introduced_in_version_9()
    {
        Assert.True(BuiltInRoleDefaults.CurrentVersion >= 9); // wave 3 bumps it to 10
        Assert.All(TimetablePermissions, p => Assert.Equal(9, BuiltInRoleDefaults.IntroducedIn(p)));
        Assert.Equal(TimetablePermissions.Order(StringComparer.Ordinal), BuiltInRoleDefaults.IntroducedInVersion9.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Timetable_catalogs_match_the_plan()
    {
        Assert.Equal(["Draft", "Published"], SlotStates.All);
        Assert.Equal(["Block", "Warn"], ConflictModes.All);
        Assert.Equal(["TeacherDoubleBooked", "ClassDoubleBooked"], ConflictKinds.All); // no room clash: rooms are free text (D60)
        Assert.Equal(["Substitute", "Cancelled", "Moved"], ExceptionKinds.All);
        Assert.Equal(["Conflict", "HasException", "DraftChange"], SlotBadges.All);
        Assert.Equal(["ClassCourseMissing", "TeacherMissing", "PeriodMissing", "SlotExists"], CopySkipReasons.All);
        Assert.Equal(["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"], WorkingDayNames.All);
        Assert.Equal(["Sun", "Mon", "Tue", "Wed", "Thu"], WorkingDayNames.Default);
        Assert.Equal((100, 500), (TimetableLimits.MaxRoomLength, TimetableLimits.MaxNoteLength));
    }

    [Fact]
    public void Timetable_settings_match_the_plan()
    {
        var days = SettingCatalog.Find("Timetable.WorkingDays")!;
        Assert.Equal((SettingValueKinds.StringSet, "[\"Sun\",\"Mon\",\"Tue\",\"Wed\",\"Thu\"]"), (days.Kind, days.DefaultJson));
        Assert.Equal(WorkingDayNames.All, days.AllowedValues);
        Assert.False(days.ClientLevelOnly); // a unit may run another school week

        var mode = SettingCatalog.Find("Timetable.ConflictMode")!;
        Assert.Equal((SettingValueKinds.Enum, "\"Block\""), (mode.Kind, mode.DefaultJson));
        Assert.Equal(ConflictModes.All, mode.AllowedValues);
    }

    [Fact]
    public void Timetable_problem_codes_exist()
    {
        Assert.Equal("timetable_conflict", ProblemCodes.timetable_conflict);
        Assert.Equal("period_overlap", ProblemCodes.period_overlap);
        Assert.Equal("period_in_use", ProblemCodes.period_in_use);
        Assert.Equal("timetable_not_published", ProblemCodes.timetable_not_published);
        Assert.Equal("exception_date_invalid", ProblemCodes.exception_date_invalid);
        Assert.Equal("exception_substitute_invalid", ProblemCodes.exception_substitute_invalid);
    }

    [Fact]
    public void Timetable_entities_are_audited()
    {
        Assert.Contains("Period", AuditCatalog.EntityTypes);
        Assert.Contains("TimetableSlot", AuditCatalog.EntityTypes);
        Assert.Contains("TimetableException", AuditCatalog.EntityTypes);
    }

    [Fact]
    public void Timetable_routes_match_the_plan()
    {
        Assert.Equal("/api/org-units/{id:guid}/periods", Routes.OrgUnitPeriodsPattern);
        Assert.Equal("/api/org-units/{id:guid}/periods/{pid:guid}", Routes.PeriodPattern);
        Assert.Equal("/api/classes/{id:guid}/timetable", Routes.ClassTimetablePattern);
        Assert.Equal("/api/classes/{id:guid}/timetable/slots", Routes.ClassTimetableSlotsPattern);
        Assert.Equal("/api/classes/{id:guid}/timetable/slots/{sid:guid}", Routes.ClassTimetableSlotPattern);
        Assert.Equal("/api/classes/{id:guid}/timetable/validate", Routes.ClassTimetableValidatePattern);
        Assert.Equal("/api/classes/{id:guid}/timetable/publish", Routes.ClassTimetablePublishPattern);
        Assert.Equal("/api/classes/{id:guid}/timetable/copy-term", Routes.ClassTimetableCopyTermPattern);
        Assert.Equal("/api/classes/{id:guid}/timetable/export", Routes.ClassTimetableExportPattern);
        Assert.Equal("/api/timetable-slots/{id:guid}/exceptions", Routes.TimetableSlotExceptionsPattern);
        Assert.Equal("/api/timetable-slots/{id:guid}/exceptions/{eid:guid}", Routes.TimetableSlotExceptionPattern);
        Assert.Equal("/api/me/timetable", Routes.MeTimetable);
        Assert.Equal("/api/students/{id:guid}/timetable", Routes.StudentTimetablePattern);

        Assert.Equal($"/api/org-units/{Id}/periods/{Other}", Routes.Period(Id, Other));
        Assert.Equal($"/api/classes/{Id}/timetable?termId={Other}", Routes.ClassTimetable(Id, Other));
        Assert.Equal($"/api/classes/{Id}/timetable", Routes.ClassTimetable(Id));
        Assert.Equal($"/api/classes/{Id}/timetable/slots/{Other}", Routes.ClassTimetableSlot(Id, Other));
        Assert.Equal($"/api/classes/{Id}/timetable/validate?termId={Other}", Routes.ClassTimetableValidate(Id, Other));
        Assert.Equal($"/api/timetable-slots/{Id}/exceptions/{Other}", Routes.TimetableSlotException(Id, Other));
        Assert.Equal("/api/me/timetable?from=2027-02-01&to=2027-02-07", Routes.MyTimetable(new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 7)));
        Assert.Equal($"/api/students/{Id}/timetable?from=2027-02-01&to=2027-02-07",
            Routes.StudentTimetable(Id, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 7)));

        StudentContractTests.AssertUniqueApiRoutes();
    }

    [Fact]
    public void Every_timetable_route_has_a_permission_doc_comment()
    {
        // The doc comment of each route constant is its row of the plan's route table; Task 40 tests every row against it.
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lms.Shared", "Routes.Timetable.cs"));
        var matches = Regex.Matches(text, @"((?:\s*///[^\n]*\n)+)\s*public const string (\w+Pattern|MeTimetable) =");
        var routes = matches.Select(m => (Name: m.Groups[2].Value, Doc: m.Groups[1].Value)).ToList();

        Assert.True(routes.Count >= 13, $"found {routes.Count} documented route constants");
        Assert.All(routes, r => Assert.Matches(@"Timetable\.\w+", r.Doc));
        Assert.Equal(Regex.Matches(text, @"public const string \w+Pattern =").Count + 1, routes.Count); // + MeTimetable
    }

    [Fact]
    public void A_conflict_discloses_details_only_for_slots_the_caller_may_see()
    {
        // Conflicts are computed across the client; a slot outside the caller's unit subtree is reported by kind only (QA review of wave 2).
        var context = new System.Reflection.NullabilityInfoContext();
        var props = typeof(SlotConflictDto).GetProperties().ToDictionary(p => p.Name, p => context.Create(p).WriteState);
        Assert.Equal(System.Reflection.NullabilityState.NotNull, props["Kind"]);
        Assert.All(new[] { "SlotId", "CourseNameAr", "CourseNameEn", "TeacherName" },
            name => Assert.Equal(System.Reflection.NullabilityState.Nullable, props[name]));

        var hidden = RoundTrip(new SlotConflictDto(ConflictKinds.TeacherDoubleBooked, null, null, null, null));
        Assert.Null(hidden.SlotId);
        Assert.Null(hidden.TeacherName);
        Assert.Equal(ConflictKinds.TeacherDoubleBooked, hidden.Kind);

        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lms.Shared", "Timetable", "TimetableDtos.cs"));
        Assert.Contains("details are disclosed only for slots inside the caller's unit subtree", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Timetable_dtos_round_trip()
    {
        var period = new PeriodDto(Id, Other, "الأولى", "First", new TimeOnly(7, 30), new TimeOnly(8, 15), 1, null);
        var savePeriod = new SavePeriodRequest("الأولى", "First", new TimeOnly(7, 30), new TimeOnly(8, 15), 1);
        var slot = new SlotDto(Id, Other, Id, Other, "رياضيات", "Math", Id, "أحمد", DayOfWeek.Sunday, Other, "مختبر 2", SlotStates.Draft, 3,
            [SlotBadges.Conflict]);
        var save = new SaveSlotRequest(Id, Other, Id, DayOfWeek.Monday, Other, null, 3);
        var clash = new SlotConflictDto(ConflictKinds.TeacherDoubleBooked, Id, "علوم", "Science", "أحمد");
        var saved = new SaveSlotResultDto(slot, [clash]);
        var grid = new ClassTimetableDto(Id, Other, WorkingDayNames.Default, [period], [slot], ConflictModes.Warn, 1, DateTime.UtcNow);
        var copy = new CopyTermResultDto(4, [new CopySkippedDto(Id, CopySkipReasons.PeriodMissing, "رياضيات", "Math")]);
        var exception = new TimetableExceptionDto(Id, Other, new DateOnly(2027, 2, 7), ExceptionKinds.Substitute, Id, "سارة", null, "إجازة");
        var saveException = new SaveTimetableExceptionRequest(new DateOnly(2027, 2, 7), ExceptionKinds.Moved, null, Other, null);
        var card = new TimetableCardDto(Id, new DateOnly(2027, 2, 7), Other, "رياضيات", "Math", "أحمد", null, ExceptionKinds.Cancelled, null, "الأول أ", "1A");
        var view = new TimetableViewDto(new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 7), WorkingDayNames.Default, [period], [card], DateTime.UtcNow);

        Assert.Equal(period, RoundTrip(period));
        Assert.Equal(new TimeOnly(7, 30), RoundTrip(savePeriod).StartTime);
        Assert.Equal("مختبر 2", RoundTrip(slot).RoomName); // a plain optional string: no room list (D60)
        Assert.Equal(DayOfWeek.Sunday, RoundTrip(slot).DayOfWeek);
        Assert.Equal(3, RoundTrip(save).Version);
        Assert.Equal(ConflictKinds.TeacherDoubleBooked, RoundTrip(saved).Warnings.Single().Kind);
        Assert.Equal(1, RoundTrip(grid).DraftCount);
        Assert.Equal(PeriodMissing(), RoundTrip(copy).Skipped.Single().Reason);
        Assert.Equal(new DateOnly(2027, 2, 7), RoundTrip(exception).Date);
        Assert.Equal(Other, RoundTrip(saveException).NewPeriodId);
        Assert.Equal(ExceptionKinds.Cancelled, RoundTrip(card).ExceptionKind);
        Assert.Null(RoundTrip(card).LiveSessionId); // no live session scheduled: both are null
        Assert.Null(RoundTrip(card).JoinOpensAtUtc);
        var opens = new DateTime(2027, 2, 7, 5, 25, 0, DateTimeKind.Utc);
        var live = RoundTrip(card with { LiveSessionId = Other, JoinOpensAtUtc = opens });
        Assert.Equal((Other, opens), (live.LiveSessionId, live.JoinOpensAtUtc));
        Assert.Equal(new DateOnly(2027, 2, 7), RoundTrip(view).To);
    }

    [Fact]
    public void Student_facing_timetable_views_carry_no_internal_ids_of_staff_or_drafts()
    {
        // A view card names the teacher but never a user id, a slot state or a version: only published slots reach it.
        var names = typeof(TimetableCardDto).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("TeacherUserId", names);
        Assert.DoesNotContain("State", names);
        Assert.DoesNotContain("Version", names);
        Assert.DoesNotContain("SubstituteUserId", names);
    }

    private static string PeriodMissing() => CopySkipReasons.PeriodMissing;

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
