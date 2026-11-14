using System.Text.Json;
using System.Text.RegularExpressions;
using Lms.Infrastructure.Access;
using Lms.Shared;
using Lms.Shared.AcademicYears;
using Lms.Shared.Auditing;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Grading;
using Lms.Shared.Notifications;

namespace Lms.UnitTests.Contracts;

/// <summary>M5 contract, wave 1 (plan Task 1): grading scales, gradebook, publishing, grade reads, reports and the client time zone.</summary>
public sealed class GradingContractTests
{
    private static readonly JsonSerializerOptions Web = JsonSerializerOptions.Web;
    private static readonly Guid Id = Guid.Parse("0198a6f2-0000-7000-8000-00000000002a");
    private static readonly Guid Other = Guid.Parse("0198a6f2-0000-7000-8000-00000000002b");

    private static readonly string[] GradePermissions =
        ["Grades.View", "Grades.Manage", "Grades.Publish", "Grades.ConfigureScales", "Grades.ViewReports", "Grades.ViewOwn"];

    private static IReadOnlyList<string> GradesOf(string role) =>
        BuiltInRoles.Get(role).DefaultPermissions.Where(p => p.StartsWith("Grades.", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();

    [Fact]
    public void Grading_permissions_follow_the_matrix()
    {
        string[] declared = [Permissions.Grades.View, Permissions.Grades.Manage, Permissions.Grades.Publish,
            Permissions.Grades.ConfigureScales, Permissions.Grades.ViewReports, Permissions.Grades.ViewOwn];
        Assert.Equal(GradePermissions, declared);
        Assert.All(GradePermissions, p => Assert.Contains(p, Permissions.All));

        string[] staffAdmin = ["Grades.ConfigureScales", "Grades.Manage", "Grades.Publish", "Grades.View", "Grades.ViewReports"];
        Assert.Equal(staffAdmin, GradesOf(BuiltInRoles.OrgAdmin));
        Assert.Equal(staffAdmin, GradesOf(BuiltInRoles.Manager));
        Assert.Equal(["Grades.Manage", "Grades.Publish", "Grades.View"], GradesOf(BuiltInRoles.Teacher));
        Assert.Equal(["Grades.View", "Grades.ViewReports"], GradesOf(BuiltInRoles.Supervisor));
        Assert.Empty(GradesOf(BuiltInRoles.Counselor)); // L16: no Grades.*
        Assert.Equal(["Grades.ViewOwn"], GradesOf(BuiltInRoles.Student));
        Assert.Equal(["Grades.ViewOwn"], GradesOf(BuiltInRoles.Guardian));
        Assert.DoesNotContain(Permissions.Grades.ConfigureScales, GradesOf(BuiltInRoles.Teacher));
        Assert.DoesNotContain(Permissions.Grades.Publish, GradesOf(BuiltInRoles.Supervisor));
    }

    [Fact]
    public void Grade_relationships_use_own_class_courses_and_supervised_courses()
    {
        Assert.Equal(Relationships.OwnClassCourses, RelationshipRules.For(BuiltInRoles.Teacher, Permissions.Grades.Manage));
        Assert.Equal(Relationships.OwnClassCourses, RelationshipRules.For(BuiltInRoles.Teacher, Permissions.Grades.Publish));
        Assert.Equal(Relationships.OwnClassCourses, RelationshipRules.For(BuiltInRoles.Teacher, Permissions.Grades.View));
        Assert.Equal(Relationships.SupervisedCourses, RelationshipRules.For(BuiltInRoles.Supervisor, Permissions.Grades.View));
        Assert.Equal(Relationships.SupervisedCourses, RelationshipRules.For(BuiltInRoles.Supervisor, Permissions.Grades.ViewReports));
        Assert.Null(RelationshipRules.For(BuiltInRoles.Manager, Permissions.Grades.Manage)); // unit scope
        Assert.Null(RelationshipRules.For(BuiltInRoles.Student, Permissions.Grades.ViewOwn)); // self, not a relationship resource
    }

    [Fact]
    public void Viewing_own_grades_is_self_service_for_the_escalation_guards()
    {
        Assert.Contains(Permissions.Grades.ViewOwn, Permissions.SelfService);
        Assert.DoesNotContain(Permissions.Grades.Manage, Permissions.SelfService);
    }

    [Fact]
    public void Grading_permissions_are_introduced_in_defaults_version_8()
    {
        Assert.True(BuiltInRoleDefaults.CurrentVersion >= 8); // later waves bump it (9 timetable, 10 attendance and excuses)
        Assert.All(GradePermissions, p => Assert.Equal(8, BuiltInRoleDefaults.IntroducedIn(p)));
        Assert.Equal(GradePermissions.Order(StringComparer.Ordinal), BuiltInRoleDefaults.IntroducedInVersion8.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Grading_catalogs_match_the_plan()
    {
        Assert.Equal(["Percentage", "Letter", "Gpa"], ScaleKinds.All);
        Assert.Equal(["Hidden", "Published", "Scheduled"], PublishStates.All);
        Assert.Equal(["Event", "Manual", "Override", "NewerScoreExists", "Excused", "Empty"], GradeCellStates.All);
        Assert.Equal(["Normalise", "Zero", "HideUntilComplete"], MissingPolicies.All);
        Assert.Equal(["PointsWeighted", "PercentAverage"], CategoryMethods.All);
        Assert.Equal(["HalfUp"], RoundingModes.All);
        Assert.Equal(["Open", "Locked"], GradebookStates.All);
        Assert.Equal(["Now", "Schedule", "Hide"], PublishModes.All);
        Assert.Equal(["Set", "Override", "ClearOverride", "Excuse", "Clear", "KeepOverride", "DropOverride"], GradeEntryActions.All);
        Assert.Equal(["Publish", "Lock"], GradeSnapshotKinds.All);
        Assert.Equal(["student", "class", "course"], GradeReportKinds.All);

        foreach (var list in new[]
                 {
                     ScaleKinds.All, PublishStates.All, GradeCellStates.All, MissingPolicies.All, CategoryMethods.All, RoundingModes.All,
                     GradebookStates.All, PublishModes.All, GradeEntryActions.All, GradeSnapshotKinds.All, GradeReportKinds.All,
                 })
        {
            Assert.Equal(list.Count, list.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void Grading_limits_match_the_plan()
    {
        Assert.Equal((0, 4, 2), (GradingLimits.DecimalPlacesMin, GradingLimits.DecimalPlacesMax, GradingLimits.DecimalPlacesDefault));
        Assert.Equal((0m, 100m), (GradingLimits.WeightMin, GradingLimits.WeightMax));
        Assert.Equal((0.01m, 1000m), (GradingLimits.MaxPointsMin, GradingLimits.MaxPointsMax));
        Assert.Equal((1, 500), (GradingLimits.ReasonMinLength, GradingLimits.ReasonMaxLength));
        Assert.Equal((50, 100), (GradingLimits.GridPageSize, GradingLimits.GridPageSizeMax));
        Assert.Equal((2000, 50, 15), (GradingLimits.ImportMaxRows, GradingLimits.ImportMaxColumns, GradingLimits.ImportTokenMinutes));
        Assert.Equal((2, 3), (GradingLimits.TermsPerYearMin, GradingLimits.TermsPerYearMax));
    }

    [Fact]
    public void Grading_settings_match_the_plan_and_the_time_zone_is_client_level()
    {
        Assert.Equal("Asia/Riyadh", SettingKeys.OrgTimeZone.Default); // D64: also when the setting is missing
        Assert.Equal("Normalise", SettingKeys.GradesMissingPolicy.Default);
        Assert.Equal("PointsWeighted", SettingKeys.GradesCategoryMethod.Default);
        Assert.Equal(2, SettingKeys.GradesDecimalPlaces.Default);
        Assert.Equal(2, SettingKeys.AcademicYearsTermsPerYear.Default); // D62

        var timeZone = SettingCatalog.Find("Org.TimeZone")!;
        Assert.True(timeZone.ClientLevelOnly);
        Assert.Equal(SettingValueKinds.String, timeZone.Kind);
        Assert.Equal("\"Asia/Riyadh\"", timeZone.DefaultJson);
        Assert.True(SettingCatalog.Find("AcademicYears.TermsPerYear")!.ClientLevelOnly);
        Assert.Equal((2, 3), (SettingCatalog.Find("AcademicYears.TermsPerYear")!.Min, SettingCatalog.Find("AcademicYears.TermsPerYear")!.Max));
        Assert.Equal(MissingPolicies.All, SettingCatalog.Find("Grades.MissingPolicy")!.AllowedValues);
        Assert.Equal(CategoryMethods.All, SettingCatalog.Find("Grades.CategoryMethod")!.AllowedValues);
        Assert.Equal((0, 4), (SettingCatalog.Find("Grades.DecimalPlaces")!.Min, SettingCatalog.Find("Grades.DecimalPlaces")!.Max));
        Assert.False(SettingCatalog.Find("Grades.MissingPolicy")!.ClientLevelOnly); // a unit may choose its own policy
        Assert.DoesNotContain(SettingCatalog.All.Where(d => d.ClientLevelOnly).Select(d => d.Name), n => n.StartsWith("Live.", StringComparison.Ordinal));
    }

    [Fact]
    public void Grading_problem_codes_exist()
    {
        Assert.Equal(
            ["gradebook_locked", "scale_provisional", "scale_bands_invalid", "scale_in_use", "weights_invalid", "grade_out_of_range",
             "grade_override_reason_required", "grade_import_invalid", "grade_import_expired", "terms_have_dependants", "concurrency_conflict"],
            new[]
            {
                ProblemCodes.gradebook_locked, ProblemCodes.scale_provisional, ProblemCodes.scale_bands_invalid, ProblemCodes.scale_in_use,
                ProblemCodes.weights_invalid, ProblemCodes.grade_out_of_range, ProblemCodes.grade_override_reason_required,
                ProblemCodes.grade_import_invalid, ProblemCodes.grade_import_expired, ProblemCodes.terms_have_dependants,
                ProblemCodes.concurrency_conflict,
            });
    }

    [Fact]
    public void Grading_entities_are_audited()
    {
        string[] audited = ["GradingScale", "WeightTemplate", "Gradebook", "GradeCategory", "GradeItem", "GradeEntry", "GradeSnapshot"];

        Assert.All(audited, e => Assert.Contains(e, AuditCatalog.EntityTypes));
    }

    [Fact]
    public void Grades_published_names_its_recipients_and_carries_the_course_name_only()
    {
        var published = NotificationEvents.Find(NotificationEvents.GradesPublished)!;

        Assert.Equal("Grades.Published", published.Key);
        Assert.Empty(published.DefaultRecipientRoles);
        Assert.Equal(["CourseNameAr", "CourseNameEn"], published.Parameters);
    }

    [Fact]
    public void Grade_notification_texts_have_no_value_placeholders()
    {
        // L26: no grade value and no item title in any notification. The parameters of the event are the only placeholders its text may use.
        var forbidden = new[] { "points", "score", "grade", "percent", "title", "item", "band", "final" };
        var parameters = NotificationEvents.Find(NotificationEvents.GradesPublished)!.Parameters;

        Assert.DoesNotContain(parameters, p => forbidden.Any(f => p.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Grading_routes_match_the_plan()
    {
        Assert.Equal("/api/org-units/{id:guid}/grading-scales", Routes.OrgUnitGradingScalesPattern);
        Assert.Equal("/api/org-units/{id:guid}/grading-scales/{sid:guid}", Routes.GradingScalePattern);
        Assert.Equal("/api/org-units/{id:guid}/grading-scales/{sid:guid}/confirm", Routes.GradingScaleConfirmPattern);
        Assert.Equal("/api/org-units/{id:guid}/grading-scales/{sid:guid}/default", Routes.GradingScaleDefaultPattern);
        Assert.Equal("/api/org-units/{id:guid}/grading-scales/presets", Routes.OrgUnitGradingScalePresetsPattern);
        Assert.Equal("/api/org-units/{id:guid}/weight-templates", Routes.OrgUnitWeightTemplatesPattern);
        Assert.Equal("/api/org-units/{id:guid}/weight-templates/{tid:guid}", Routes.WeightTemplatePattern);
        Assert.Equal("/api/class-courses/{id:guid}/gradebook", Routes.ClassCourseGradebookPattern);
        Assert.Equal("/api/class-courses/{id:guid}/gradebook/config", Routes.ClassCourseGradebookConfigPattern);
        Assert.Equal("/api/class-courses/{id:guid}/gradebook/categories", Routes.ClassCourseGradebookCategoriesPattern);
        Assert.Equal("/api/class-courses/{id:guid}/gradebook/items", Routes.ClassCourseGradebookItemsPattern);
        Assert.Equal("/api/class-courses/{id:guid}/gradebook/items/{iid:guid}", Routes.ClassCourseGradebookItemPattern);
        Assert.Equal("/api/class-courses/{id:guid}/gradebook/entries", Routes.ClassCourseGradebookEntriesPattern);
        Assert.Equal("/api/class-courses/{id:guid}/gradebook/items/publish", Routes.ClassCourseGradebookPublishPattern);
        Assert.Equal("/api/class-courses/{id:guid}/gradebook/students/{sid:guid}/student-view", Routes.ClassCourseGradebookStudentViewPattern);
        Assert.Equal("/api/class-courses/{id:guid}/gradebook/items/{iid:guid}/export", Routes.ClassCourseGradebookItemExportPattern);
        Assert.Equal("/api/class-courses/{id:guid}/gradebook/items/{iid:guid}/import/preview", Routes.ClassCourseGradebookItemImportPreviewPattern);
        Assert.Equal("/api/class-courses/{id:guid}/gradebook/items/{iid:guid}/import/apply", Routes.ClassCourseGradebookItemImportApplyPattern);
        Assert.Equal("/api/me/grades", Routes.MeGrades);
        Assert.Equal("/api/students/{id:guid}/grades", Routes.StudentGradesPattern);
        Assert.Equal("/api/grade-reports/{kind}", Routes.GradeReportPattern);
        Assert.Equal("/api/grade-reports/{kind}/export", Routes.GradeReportExportPattern);
        Assert.Equal("/api/academic-years/{id:guid}/term-structure", Routes.AcademicYearTermStructurePattern);

        Assert.Equal($"/api/org-units/{Id}/grading-scales/{Other}/confirm", Routes.GradingScaleConfirm(Id, Other));
        Assert.Equal($"/api/class-courses/{Id}/gradebook/items/{Other}/import/apply", Routes.ClassCourseGradebookItemImportApply(Id, Other));
        Assert.Equal($"/api/class-courses/{Id}/gradebook?termId={Other}&page=2&pageSize=50", Routes.ClassCourseGradebook(Id, Other, 2));
        Assert.Equal($"/api/class-courses/{Id}/gradebook?page=1&pageSize=50", Routes.ClassCourseGradebook(Id, null));
        Assert.Equal($"/api/students/{Id}/grades?termId={Other}", Routes.StudentGrades(Id, Other));
        Assert.Equal("/api/grade-reports/class/export", Routes.GradeReportExport(GradeReportKinds.Class));

        StudentContractTests.AssertUniqueApiRoutes();
    }

    [Fact]
    public void Every_grading_route_has_a_permission_doc_comment()
    {
        // The doc comment of each route constant is its row of the plan's route table; Task 40 tests every row against it.
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lms.Shared", "Routes.Grading.cs"));
        var matches = Regex.Matches(text, @"((?:\s*///[^\n]*\n)+)\s*public const string (\w+Pattern|MeGrades) =");
        var routes = matches.Select(m => (Name: m.Groups[2].Value, Doc: m.Groups[1].Value)).ToList();

        Assert.True(routes.Count >= 21, $"found {routes.Count} documented route constants");
        Assert.All(routes, r => Assert.Matches(@"Grades\.\w+|AcademicYears\.\w+", r.Doc));
        Assert.Equal(
            Regex.Matches(text, @"public const string \w+Pattern =").Count + 1, // + MeGrades
            routes.Count);
    }

    [Fact]
    public void Grading_dtos_round_trip()
    {
        var band = new GradingScaleBandDto(Id, 1, 90m, 100m, "A", "ممتاز", "Excellent", "مستوى متقدم", "Advanced", 5m);
        var scale = new GradingScaleDto(Id, Other, "المئوية", "Percentage", ScaleKinds.Percentage, true, false, true, "school-5-band", 1, true, null, false, [band]);
        var save = new SaveGradingScaleRequest("ثلاثة", "Three", ScaleKinds.Gpa, [new SaveGradingScaleBandRequest(1, 0m, 100m, "P", null, null, null, null, 4m)]);
        var template = new WeightTemplateDto(Id, Other, "قالب", "Template", 100m, null, [new WeightTemplateItemDto(Id, "اختبارات", "Exams", 60m, 1)]);
        var config = new GradebookConfigDto(Id, "س", "Scale", true, 2, CategoryMethods.PointsWeighted, MissingPolicies.Normalise, RoundingModes.HalfUp);
        var category = new GradeCategoryDto(Id, "غير مصنف", "Uncategorised", 0m, 0, true);
        var item = new GradeItemDto(Id, Other, "واجب", "Homework", "Assignment", Other, false, 10m, PublishStates.Hidden, null, null, 1);
        var cell = new GradeCellDto(Id, 8.5m, GradeCellStates.Override, "regrade");
        var row = new GradeRowDto(Id, "سارة", "Sara", "000123", [cell], [new GradeCategoryResultDto(Other, 85m, 51m)], 85m, "A", "ممتاز", "Excellent", 5m, 7);
        var book = new GradebookDto(Id, Other, Id, "الفصل الأول", "Term 1", config, [category], [item], new PagedResult<GradeRowDto>([row], 1, 1, 50),
            true, 100m, GradebookStates.Open, null, 0, true, true, DateTime.UtcNow, DateTime.UtcNow);
        var upsert = new UpsertEntriesRequest(Id, 7, [new GradeEntryChange(Other, GradeEntryActions.Override, 9m, "regrade")]);
        var publish = new PublishItemsRequest([Id, Other], PublishModes.Schedule, new DateTime(2027, 1, 5, 6, 0, 0, DateTimeKind.Utc), true);
        var preview = new GradeImportPreviewDto("tok", DateTime.UtcNow, 30, 2, ["x1"], [], [new GradeImportRowDto(3, "000999", null, false, "unknown_student")], []);
        var studentCourse = new StudentCourseGradesDto(Id, Other, "رياضيات", "Math", Other, "الفصل الأول", "Term 1", 2,
            [new PublishedGradeItemDto(Id, "واجب", "Homework", null, 8m, 10m, false, DateTime.UtcNow)],
            [new StudentGradeCategoryDto(Other, "أعمال", "Work", 40m, 80m)], 80m, "B", "جيد جدًا", "Very good", 4m,
            new BandLegendDto("س", "Scale", ScaleKinds.Letter, [new BandLegendEntryDto("B", 80m, 89.99m, null, null, null, null, 4m)]));
        var grades = new StudentGradesDto(Id, "سارة", "Sara", [studentCourse], DateTime.UtcNow, DateTime.UtcNow);
        var report = new GradeReportDto(GradeReportKinds.Class, new PagedResult<GradeReportRowDto>(
            [new GradeReportRowDto(Id, "سارة", "Sara", "000123", Other, "رياضيات", "Math", 80m, "B", null, null, 4m)], 1, 1, 50), DateTime.UtcNow, DateTime.UtcNow);
        var structure = new TermStructureDto(3, [new TermNameDto(1, "الفصل الأول", "Term 1"), new TermNameDto(2, "الفصل الثاني", "Term 2"), new TermNameDto(3, "الفصل الثالث", "Term 3")]);

        Assert.Equal(band, RoundTrip(scale).Bands.Single());
        Assert.True(RoundTrip(scale).Provisional);
        Assert.Equal(4m, RoundTrip(save).Bands.Single().Points);
        Assert.Equal(60m, RoundTrip(template).Items.Single().Weight);
        Assert.Equal(config, RoundTrip(config));
        Assert.True(RoundTrip(category).IsUncategorised);
        Assert.Equal(PublishStates.Hidden, RoundTrip(item).PublishState);
        Assert.Equal("regrade", RoundTrip(cell).Reason);
        Assert.Equal(7, RoundTrip(row).Version);
        Assert.Equal(50, RoundTrip(book).Rows.PageSize);
        Assert.Equal(GradeEntryActions.Override, RoundTrip(upsert).Changes.Single().Action);
        Assert.True(RoundTrip(publish).AcknowledgeProvisionalScale);
        Assert.Equal(PublishModes.Schedule, RoundTrip(publish).Mode);
        Assert.Equal("unknown_student", RoundTrip(preview).Errors.Single().ErrorCode);
        Assert.Equal(4m, RoundTrip(grades).Courses.Single().BandPoints);
        Assert.Equal(GradeReportKinds.Class, RoundTrip(report).Kind);
        Assert.Equal(3, RoundTrip(structure).TermNames.Count);
        Assert.Equal((1, GradingLimits.GridPageSize), (new GradebookQuery().Page, new GradebookQuery().PageSize));
        Assert.Equal((1, Paging.DefaultPageSize), (new GradeReportQuery().Page, new GradeReportQuery().PageSize));
        Assert.Equal(new PublishResultDto(2, 25, 25), RoundTrip(new PublishResultDto(2, 25, 25)));
    }

    [Fact]
    public void Student_facing_grade_dtos_carry_nothing_staff_only()
    {
        // L6: a student or guardian never receives hidden items, markers, counts, ranks or class statistics.
        Type[] studentFacing =
        [
            typeof(StudentGradesDto), typeof(StudentCourseGradesDto), typeof(PublishedGradeItemDto), typeof(StudentGradeCategoryDto),
            typeof(BandLegendDto), typeof(BandLegendEntryDto),
        ];
        string[] staffOnly = ["PublishState", "PublishAt", "Newer", "Rank", "Average", "Minimum", "Maximum", "Median", "Hidden", "Override", "Reason", "EventsAfterLock", "Provisional", "SourceKind", "SourceId", "Version", "Count"];

        Assert.All(studentFacing, t => Assert.DoesNotContain(t.GetProperties(), p =>
            staffOnly.Any(f => p.Name.Contains(f, StringComparison.OrdinalIgnoreCase))));
    }

    [Fact]
    public void Student_dto_docs_pin_published_only_semantics_for_categories_and_the_final()
    {
        // QA1 on #625: a category of hidden items, a HideUntilComplete final that waits on hidden items, or weights of hidden categories would
        // disclose that hidden items exist. The engine (Task 6) and the leak tests (Task 41) are written against these pinned sentences.
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lms.Shared", "Grading", "GradeReadDtos.cs"));

        Assert.Contains("Only categories that hold at least one published item are listed", text, StringComparison.Ordinal);
        Assert.Contains("every published item has a score", text, StringComparison.Ordinal);
        Assert.Contains("renormalised over the listed categories", text, StringComparison.Ordinal);
        Assert.Contains("no count, id, name, weight or", text, StringComparison.Ordinal);
        // The student shape has nowhere to carry a hidden item: no hidden-category flag, no total item count, no hidden weight.
        Assert.DoesNotContain(typeof(StudentGradeCategoryDto).GetProperties(), p => p.Name is "IsHidden" or "ItemCount" or "HiddenWeight");
        Assert.DoesNotContain(typeof(StudentCourseGradesDto).GetProperties(), p => p.Name.Contains("Total", StringComparison.Ordinal) || p.Name.Contains("Pending", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_timed_grade_dto_carries_the_server_clock()
    {
        Type[] timed = [typeof(GradebookDto), typeof(StudentGradesDto), typeof(GradeReportDto)];

        Assert.All(timed, t => Assert.Contains(t.GetProperties(), p => p.Name == "ServerNowUtc" && p.PropertyType == typeof(DateTime)));
        Assert.All(timed, t => Assert.Contains(t.GetProperties(), p => p.Name == "ComputedAtUtc" && p.PropertyType == typeof(DateTime)));
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
