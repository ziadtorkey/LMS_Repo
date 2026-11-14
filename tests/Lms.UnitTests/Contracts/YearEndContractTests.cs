using System.Reflection;
using System.Text.Json;
using Lms.Infrastructure.Access;
using Lms.Shared;
using Lms.Shared.Auditing;
using Lms.Shared.Authorization;
using Lms.Shared.Notifications;
using Lms.Shared.YearEnd;

namespace Lms.UnitTests.Contracts;

/// <summary>M7 contract, wave 1: close year, grade-change approval, promotion, withdrawal, graduation, archive, re-admission and opening the next year (PRD D78 to D87; FE proposal #808).</summary>
public sealed class YearEndContractTests
{
    private static readonly JsonSerializerOptions Web = JsonSerializerOptions.Web;
    private static readonly Guid Id = Guid.Parse("0198a6f2-0000-7000-8000-00000000003a");
    private static readonly Guid Other = Guid.Parse("0198a6f2-0000-7000-8000-00000000003b");

    private static IReadOnlyList<string> Held(string role, params string[] among) =>
        BuiltInRoles.Get(role).DefaultPermissions.Where(among.Contains).Order(StringComparer.Ordinal).ToList();

    private static readonly string[] M7Permissions =
    [
        Permissions.AcademicYears.Close, Permissions.Grades.ChangeRequest, Permissions.Grades.ChangeApprove, Permissions.Promotion.Run,
        Permissions.Students.Withdraw, Permissions.Students.Readmit, Permissions.Students.ViewArchive,
    ];

    [Fact]
    public void Permissions_exist_in_the_catalog_with_the_wave_1_names()
    {
        Assert.Equal(
            ["AcademicYears.Close", "Grades.ChangeRequest", "Grades.ChangeApprove", "Promotion.Run", "Students.Withdraw", "Students.Readmit", "Students.ViewArchive"],
            M7Permissions);
        Assert.All(M7Permissions, p => Assert.Contains(p, Permissions.All));
        Assert.All(M7Permissions, p => Assert.DoesNotContain(p, Permissions.SelfService)); // staff permissions: none reaches only the holder's own data
    }

    [Fact]
    public void Only_the_Org_Admin_closes_a_year_D17()
    {
        Assert.Equal([Permissions.AcademicYears.Close], Held(BuiltInRoles.OrgAdmin, Permissions.AcademicYears.Close));
        foreach (var role in new[] { BuiltInRoles.Manager, BuiltInRoles.Supervisor, BuiltInRoles.Counselor, BuiltInRoles.Teacher, BuiltInRoles.Student, BuiltInRoles.Guardian })
            Assert.Empty(Held(role, Permissions.AcademicYears.Close));
    }

    [Fact]
    public void Promotion_is_run_by_the_Org_Admin_and_the_Manager_D78()
    {
        Assert.Equal([Permissions.Promotion.Run], Held(BuiltInRoles.OrgAdmin, Permissions.Promotion.Run));
        Assert.Equal([Permissions.Promotion.Run], Held(BuiltInRoles.Manager, Permissions.Promotion.Run));
        foreach (var role in new[] { BuiltInRoles.Supervisor, BuiltInRoles.Counselor, BuiltInRoles.Teacher, BuiltInRoles.Student, BuiltInRoles.Guardian })
            Assert.Empty(Held(role, Permissions.Promotion.Run));
    }

    [Fact]
    public void Grade_changes_are_requested_by_teachers_and_approved_by_the_Manager_or_Org_Admin_D80()
    {
        Assert.Equal([Permissions.Grades.ChangeRequest], Held(BuiltInRoles.Teacher, Permissions.Grades.ChangeRequest, Permissions.Grades.ChangeApprove));
        foreach (var role in new[] { BuiltInRoles.OrgAdmin, BuiltInRoles.Manager })
            Assert.Equal([Permissions.Grades.ChangeApprove, Permissions.Grades.ChangeRequest], Held(role, Permissions.Grades.ChangeRequest, Permissions.Grades.ChangeApprove));
        foreach (var role in new[] { BuiltInRoles.Supervisor, BuiltInRoles.Counselor, BuiltInRoles.Student, BuiltInRoles.Guardian })
            Assert.Empty(Held(role, Permissions.Grades.ChangeRequest, Permissions.Grades.ChangeApprove));
    }

    [Fact]
    public void Withdrawal_is_for_the_Org_Admin_Manager_and_Counselor_re_admission_for_the_first_two_D82()
    {
        foreach (var role in new[] { BuiltInRoles.OrgAdmin, BuiltInRoles.Manager })
            Assert.Equal([Permissions.Students.Readmit, Permissions.Students.ViewArchive, Permissions.Students.Withdraw],
                Held(role, Permissions.Students.Withdraw, Permissions.Students.Readmit, Permissions.Students.ViewArchive));
        Assert.Equal([Permissions.Students.ViewArchive, Permissions.Students.Withdraw],
            Held(BuiltInRoles.Counselor, Permissions.Students.Withdraw, Permissions.Students.Readmit, Permissions.Students.ViewArchive));
        Assert.Equal([Permissions.Students.ViewArchive], Held(BuiltInRoles.Supervisor, Permissions.Students.Withdraw, Permissions.Students.Readmit, Permissions.Students.ViewArchive));
        foreach (var role in new[] { BuiltInRoles.Teacher, BuiltInRoles.Student, BuiltInRoles.Guardian })
            Assert.Empty(Held(role, Permissions.Students.Withdraw, Permissions.Students.Readmit, Permissions.Students.ViewArchive));
    }

    [Fact]
    public void Every_wave_1_permission_is_introduced_in_defaults_version_12_and_the_current_version_is_untouched()
    {
        Assert.All(M7Permissions, p => Assert.Equal(12, BuiltInRoleDefaults.IntroducedIn(p)));
        Assert.Equal(M7Permissions.Append(Permissions.Students.ManageWithdrawalReasons).Order(StringComparer.Ordinal), BuiltInRoleDefaults.IntroducedInVersion12.Order(StringComparer.Ordinal)); // wave 2 adds the reasons permission
        // The Lead sets CurrentVersion to 12 after M6 (version 11) lands; this wave must not change it.
        Assert.True(BuiltInRoleDefaults.CurrentVersion < 12);
    }

    [Fact]
    public void Routes_are_unique_and_under_the_expected_prefixes()
    {
        var all = typeof(Routes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (f.Name, Value: (string)f.GetRawConstantValue()!)).ToList();
        string[] mine =
        [
            nameof(Routes.AcademicYearClosePreviewPattern), nameof(Routes.AcademicYearClosePattern), nameof(Routes.AcademicYearClosingReportPattern),
            nameof(Routes.AcademicYearOpenNextPreviewPattern), nameof(Routes.AcademicYearOpenNextPattern), nameof(Routes.AcademicYearOpenNextRunPattern),
            nameof(Routes.ClassCourseGradeChangeRequestsPattern), nameof(Routes.GradeChangeRequests), nameof(Routes.GradeChangeRequestPattern),
            nameof(Routes.GradeChangeApprovePattern), nameof(Routes.GradeChangeRejectPattern),
            nameof(Routes.PromotionRuns), nameof(Routes.PromotionRunPattern), nameof(Routes.PromotionRunRowsPattern), nameof(Routes.PromotionRunDecisionsPattern),
            nameof(Routes.PromotionRunDecisionsFilterPattern), nameof(Routes.PromotionRunExecutePattern), nameof(Routes.PromotionRunUndoPattern), nameof(Routes.PromotionRunFailuresPattern),
            nameof(Routes.StudentWithdrawPattern), nameof(Routes.StudentGraduatePattern), nameof(Routes.StudentReadmitPattern), nameof(Routes.StudentsArchive), nameof(Routes.WithdrawalReasons),
        ];
        var values = all.Where(r => mine.Contains(r.Name)).Select(r => r.Value).ToList();
        Assert.Equal(mine.Length, values.Count);
        Assert.Equal(values.Count, values.Distinct(StringComparer.Ordinal).Count());
        Assert.All(values, v => Assert.StartsWith("/api/", v));
        // No clash with any other route of the contract.
        Assert.Equal(all.Count, all.Select(r => r.Value).Count()); // (sanity: the reflection saw every literal)
        var others = all.Where(r => !mine.Contains(r.Name)).Select(r => r.Value).ToHashSet(StringComparer.Ordinal);
        Assert.All(values, v => Assert.DoesNotContain(v, others));
    }

    [Fact]
    public void Route_helpers_match_the_patterns()
    {
        string Fill(string pattern, params (string Name, Guid Value)[] ids) =>
            ids.Aggregate(pattern, (p, i) => p.Replace("{" + i.Name + ":guid}", i.Value.ToString()));

        Assert.Equal(Fill(Routes.AcademicYearClosePreviewPattern, ("id", Id)), Routes.AcademicYearClosePreview(Id));
        Assert.Equal(Fill(Routes.AcademicYearClosePattern, ("id", Id)), Routes.AcademicYearClose(Id));
        Assert.Equal(Fill(Routes.AcademicYearClosingReportPattern, ("id", Id)), Routes.AcademicYearClosingReport(Id));
        Assert.Equal(Fill(Routes.AcademicYearOpenNextPreviewPattern, ("id", Id)), Routes.AcademicYearOpenNextPreview(Id));
        Assert.Equal(Fill(Routes.AcademicYearOpenNextPattern, ("id", Id)), Routes.AcademicYearOpenNext(Id));
        Assert.Equal(Fill(Routes.AcademicYearOpenNextRunPattern, ("id", Id), ("runId", Other)), Routes.AcademicYearOpenNextRun(Id, Other));
        Assert.Equal(Fill(Routes.ClassCourseGradeChangeRequestsPattern, ("id", Id)), Routes.ClassCourseGradeChangeRequests(Id));
        Assert.Equal(Fill(Routes.GradeChangeRequestPattern, ("id", Id)), Routes.GradeChangeRequest(Id));
        Assert.Equal(Fill(Routes.GradeChangeApprovePattern, ("id", Id)), Routes.GradeChangeApprove(Id));
        Assert.Equal(Fill(Routes.GradeChangeRejectPattern, ("id", Id)), Routes.GradeChangeReject(Id));
        Assert.Equal(Fill(Routes.PromotionRunPattern, ("id", Id)), Routes.PromotionRun(Id));
        Assert.Equal(Fill(Routes.PromotionRunRowsPattern, ("id", Id)), Routes.PromotionRunRows(Id));
        Assert.Equal(Fill(Routes.PromotionRunDecisionsPattern, ("id", Id)), Routes.PromotionRunDecisions(Id));
        Assert.Equal(Fill(Routes.PromotionRunDecisionsFilterPattern, ("id", Id)), Routes.PromotionRunDecisionsFilter(Id));
        Assert.Equal(Fill(Routes.PromotionRunExecutePattern, ("id", Id)), Routes.PromotionRunExecute(Id));
        Assert.Equal(Fill(Routes.PromotionRunUndoPattern, ("id", Id)), Routes.PromotionRunUndo(Id));
        Assert.Equal(Fill(Routes.PromotionRunFailuresPattern, ("id", Id)), Routes.PromotionRunFailures(Id));
        Assert.Equal(Fill(Routes.StudentWithdrawPattern, ("id", Id)), Routes.StudentWithdraw(Id));
        Assert.Equal(Fill(Routes.StudentGraduatePattern, ("id", Id)), Routes.StudentGraduate(Id));
        Assert.Equal(Fill(Routes.StudentReadmitPattern, ("id", Id)), Routes.StudentReadmit(Id));
    }

    [Fact]
    public void Catalogs_hold_the_agreed_values_in_order()
    {
        Assert.Equal(["Pending", "Approved", "Rejected"], GradeChangeStates.All);
        Assert.Equal(["Promote", "Repeat", "Graduate", "TransferOut"], PromotionOutcomes.All);
        Assert.Equal(["Draft", "Running", "Done", "PartiallyFailed", "Undone", "Failed"], PromotionRunStates.All);
        Assert.Equal(["Withdrawn", "Graduated", "TransferredOut"], StudentExitKinds.All);
        Assert.Equal(["Classes", "ClassCourses", "Blueprints", "TimetablePeriods"], YearCopyParts.All);
        Assert.Equal(10, PromotionLimits.DefaultUndoWindowMinutes); // D12
        Assert.Equal(200, PromotionLimits.DecisionsPerCall);
        Assert.Equal(20, GradeChangeLimits.ReasonMinLength);
        Assert.Equal(500, GradeChangeLimits.ReasonMaxLength);
    }

    [Fact]
    public void Dtos_round_trip_through_json()
    {
        var now = new DateTime(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
        var run = new PromotionRunDto(Id, Id, Other, Other, true, PromotionRunStates.Done, 10, 10, 1, new PromotionCountsDto(6, 1, 2, 1, 0),
            [new YearEndIssueDto(YearEndIssueCodes.NoLevelMapping, 1, Id)], [new YearEndIssueDto(YearEndIssueCodes.OverCapacity, 2, null)], now.AddMinutes(10), now, now, Id, now);
        var run2 = JsonSerializer.Deserialize<PromotionRunDto>(JsonSerializer.Serialize(run, Web), Web)!;
        Assert.Equal(run.Counts, run2.Counts);
        Assert.Equal(run.UndoUntilUtc, run2.UndoUntilUtc);
        Assert.Equal(YearEndIssueCodes.NoLevelMapping, run2.Blocking[0].Code);

        var withdraw = new WithdrawStudentRequest(WithdrawalReasons.Other, "moved abroad", new DateOnly(2026, 12, 1));
        Assert.Equal(withdraw, JsonSerializer.Deserialize<WithdrawStudentRequest>(JsonSerializer.Serialize(withdraw, Web), Web));

        var open = new OpenNextYearRequest("٢٠٢٧", "2027", new DateOnly(2027, 9, 1), new DateOnly(2028, 6, 30), Id, [YearCopyParts.Classes], [], null, null);
        var open2 = JsonSerializer.Deserialize<OpenNextYearRequest>(JsonSerializer.Serialize(open, Web), Web)!;
        Assert.Equal(open.CopyFromYearId, open2.CopyFromYearId);
        Assert.Equal(open.CopyParts, open2.CopyParts);

        var rows = new PromotionRowsQuery(PromotionOutcomes.Repeat, Id, true, "ali", 2, 50);
        Assert.Equal(rows, JsonSerializer.Deserialize<PromotionRowsQuery>(JsonSerializer.Serialize(rows, Web), Web));
    }

    [Fact]
    public void Preview_and_run_dtos_carry_no_grade_values_and_no_national_ids()
    {
        // Only a grade-change request carries values (to the staff allowed to read the grade); the promotion, the archive and the closing report never do.
        static IEnumerable<string> Props(Type t) => t.GetProperties().Select(p => p.Name);
        foreach (var t in new[] { typeof(PromotionRunDto), typeof(PromotionRowDto), typeof(PromotionFailureDto), typeof(ArchivedStudentDto), typeof(ClosingReportDto), typeof(ClosingReportRowDto), typeof(YearClosePreviewDto) })
        {
            Assert.DoesNotContain(Props(t), n => n.Contains("Score", StringComparison.OrdinalIgnoreCase) || n.Contains("NationalId", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Mobile", StringComparison.OrdinalIgnoreCase) || n.Contains("Email", StringComparison.OrdinalIgnoreCase));
        }

        Assert.Contains(nameof(GradeChangeRequestDto.OldValue), Props(typeof(GradeChangeRequestDto)));
    }

    [Fact]
    public void Run_dtos_carry_the_server_clock_for_the_undo_countdown()
    {
        Assert.Contains(nameof(PromotionRunDto.ServerNowUtc), typeof(PromotionRunDto).GetProperties().Select(p => p.Name));
        Assert.Contains(nameof(PromotionRunDto.UndoUntilUtc), typeof(PromotionRunDto).GetProperties().Select(p => p.Name));
        Assert.Contains(nameof(NewYearRunDto.ServerNowUtc), typeof(NewYearRunDto).GetProperties().Select(p => p.Name));
        Assert.Contains(nameof(GradeChangeRequestDto.ServerNowUtc), typeof(GradeChangeRequestDto).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public void Problem_codes_are_declared_with_values_equal_to_their_names()
    {
        string[] codes =
        [
            ProblemCodes.year_close_blocked, ProblemCodes.year_not_closed, ProblemCodes.grade_change_pending, ProblemCodes.grade_change_not_allowed,
            ProblemCodes.promotion_in_progress, ProblemCodes.promotion_conflict, ProblemCodes.promotion_undo_window_over, ProblemCodes.promotion_undo_activity_exists,
            ProblemCodes.student_already_inactive, ProblemCodes.effective_date_invalid, ProblemCodes.readmit_class_full, ProblemCodes.readmit_year_closed,
            ProblemCodes.copy_nothing_selected, ProblemCodes.provider_test_failed,
        ];
        Assert.Equal(
            ["year_close_blocked", "year_not_closed", "grade_change_pending", "grade_change_not_allowed", "promotion_in_progress", "promotion_conflict",
             "promotion_undo_window_over", "promotion_undo_activity_exists", "student_already_inactive", "effective_date_invalid", "readmit_class_full",
             "readmit_year_closed", "copy_nothing_selected", "provider_test_failed"],
            codes);
    }

    [Fact]
    public void New_audited_entities_are_in_the_audit_catalog()
    {
        Assert.Contains("GradeChangeRequest", AuditCatalog.EntityTypes);
        Assert.Contains("PromotionRun", AuditCatalog.EntityTypes);
        Assert.Contains("StudentExit", AuditCatalog.EntityTypes);
    }

    [Fact]
    public void Notification_events_are_declared_with_named_or_role_recipients_and_no_grade_value()
    {
        var requested = NotificationEvents.Find(NotificationEvents.GradesChangeRequested)!;
        Assert.Equal("Grades.ChangeRequested", requested.Key);
        Assert.Equal([BuiltInRoles.Manager, BuiltInRoles.OrgAdmin], requested.DefaultRecipientRoles);
        Assert.DoesNotContain(requested.Parameters, p => p.Contains("Value", StringComparison.Ordinal) || p.Contains("Score", StringComparison.Ordinal));

        var decided = NotificationEvents.Find(NotificationEvents.GradesChangeDecided)!;
        Assert.Empty(decided.DefaultRecipientRoles); // the requester, a named recipient
        Assert.Contains("Decision", decided.Parameters);

        Assert.Empty(NotificationEvents.Find(NotificationEvents.PromotionFinished)!.DefaultRecipientRoles);
        Assert.Equal([BuiltInRoles.OrgAdmin], NotificationEvents.Find(NotificationEvents.YearsNextOpened)!.DefaultRecipientRoles);
    }
}
