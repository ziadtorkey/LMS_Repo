using Lms.Client.Services.Api;
using Lms.Client.Services.YearEnd;
using Lms.Shared;
using Lms.Shared.YearEnd;

namespace Lms.Client.Tests.YearEnd;

/// <summary>The year-end fakes mimic the server rules the pages depend on (M7 H1; PRD D12, D78 to D84; contract #812).</summary>
public sealed class FakeYearEndApiTests
{
    private static readonly DateTime T0 = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Year = FakeYearCloseApi.DemoYearId;

    private static async Task<ApiProblemException> Fails(Func<Task> action) => await Assert.ThrowsAsync<ApiProblemException>(action);

    // --- close year

    [Fact]
    public async Task Close_needs_the_year_name_typed()
    {
        var api = new FakeYearCloseApi { Now = () => T0 };

        var ex = await Fails(() => api.CloseAsync(Year, new CloseYearRequest("wrong")));

        Assert.Equal(400, ex.Status);
        Assert.Equal(nameof(CloseYearRequest.ConfirmName), ex.Field);
        Assert.False(api.IsClosed(Year));
    }

    [Fact]
    public async Task Close_locks_the_year_and_a_second_close_is_year_closed()
    {
        var api = new FakeYearCloseApi { Now = () => T0 };

        var closed = await api.CloseAsync(Year, new CloseYearRequest(" 2026/2027 "));

        Assert.True(api.IsClosed(Year));
        Assert.Equal(T0, closed.ClosedAtUtc);
        Assert.Equal(ProblemCodes.year_closed, (await Fails(() => api.CloseAsync(Year, new CloseYearRequest("2026/2027")))).Code);
    }

    [Fact]
    public async Task A_blocker_refuses_the_close_and_unpublished_grades_only_warn_D79()
    {
        var api = new FakeYearCloseApi();
        api.Blocking.Add(new YearEndIssueDto("OpenTerms", 1, null));

        var preview = await api.GetPreviewAsync(Year);
        var ex = await Fails(() => api.CloseAsync(Year, new CloseYearRequest("2026/2027")));

        Assert.Contains(preview.Warnings, w => w.Code == YearEndIssueCodes.UnpublishedGradeItems);
        Assert.Equal(ProblemCodes.year_close_blocked, ex.Code);
        Assert.Equal(["OpenTerms"], ex.Codes);

        api.Blocking.Clear();
        await api.CloseAsync(Year, new CloseYearRequest("2026/2027")); // the warning alone never blocks
    }

    [Fact]
    public async Task Closing_report_exists_only_after_the_close()
    {
        var api = new FakeYearCloseApi();
        Assert.Equal(ProblemCodes.year_not_closed, (await Fails(() => api.GetReportAsync(Year))).Code);

        await api.CloseAsync(Year, new CloseYearRequest("2026/2027"));

        Assert.NotEmpty((await api.GetReportAsync(Year)).Classes);
    }

    // --- grade changes

    private static CreateGradeChangeRequest Change(string reason = "The exam paper was marked wrongly, see the review.") => new(Guid.Parse("00000000-0000-0000-0000-0000000f0d01"), Guid.Parse("00000000-0000-0000-0000-0000000f0d02"), 70m, reason, 1);

    [Fact]
    public async Task A_change_request_needs_a_locked_gradebook_and_a_20_character_reason()
    {
        var api = new FakeGradeChangesApi { Now = () => T0 };
        var course = Guid.NewGuid();

        Assert.Equal(nameof(CreateGradeChangeRequest.Reason), (await Fails(() => api.CreateAsync(course, Change("too short")))).Field);
        api.Locked = false;
        Assert.Equal(ProblemCodes.grade_change_not_allowed, (await Fails(() => api.CreateAsync(course, Change()))).Code);
        api.Locked = true;
        var created = await api.CreateAsync(course, Change());

        Assert.Equal(GradeChangeStates.Pending, created.State);
        Assert.Equal(50m, created.OldValue);
        Assert.Equal(70m, created.NewValue);
    }

    [Fact]
    public async Task One_open_request_per_grade()
    {
        var api = new FakeGradeChangesApi();
        await api.CreateAsync(Guid.NewGuid(), Change());

        Assert.Equal(ProblemCodes.grade_change_pending, (await Fails(() => api.CreateAsync(Guid.NewGuid(), Change()))).Code);
    }

    [Fact]
    public async Task The_requester_cannot_decide_their_own_request_D80()
    {
        var api = new FakeGradeChangesApi { CanApprove = true };
        var created = await api.CreateAsync(Guid.NewGuid(), Change());

        var ex = await Fails(() => api.ApproveAsync(created.Id, new DecideGradeChangeRequest(null)));

        Assert.Equal(403, ex.Status);
        Assert.Equal(ProblemCodes.grade_change_not_allowed, ex.Code);
    }

    [Fact]
    public async Task Another_approver_approves_and_a_rejection_needs_a_note()
    {
        var api = new FakeGradeChangesApi { Now = () => T0 };
        var teacher = api.Me;
        var first = await api.CreateAsync(Guid.NewGuid(), Change());
        api.Me = Guid.NewGuid();
        api.CanApprove = true;

        Assert.Equal(nameof(DecideGradeChangeRequest.Note), (await Fails(() => api.RejectAsync(first.Id, new DecideGradeChangeRequest(" ")))).Field);
        var approved = await api.ApproveAsync(first.Id, new DecideGradeChangeRequest(null));

        Assert.Equal(GradeChangeStates.Approved, approved.State);
        Assert.Equal(api.Me, approved.DecidedByUserId);
        Assert.NotEqual(teacher, approved.DecidedByUserId);
        Assert.Equal(ProblemCodes.concurrency_conflict, (await Fails(() => api.ApproveAsync(first.Id, new DecideGradeChangeRequest(null)))).Code); // already decided
    }

    [Fact]
    public async Task A_requester_lists_only_their_own_requests_and_an_approver_all()
    {
        var api = new FakeGradeChangesApi();
        await api.CreateAsync(Guid.NewGuid(), Change());
        api.Me = Guid.NewGuid();

        Assert.Empty((await api.ListAsync(new GradeChangeQuery())).Items);
        api.CanApprove = true;
        Assert.Single((await api.ListAsync(new GradeChangeQuery(GradeChangeStates.Pending))).Items);
        Assert.Empty((await api.ListAsync(new GradeChangeQuery(GradeChangeStates.Approved))).Items);
    }

    // --- promotion

    private static CreatePromotionRunRequest Create() => new(FakePromotionApi.SourceYearId, FakePromotionApi.UnitId, FakePromotionApi.TargetYearId, false);

    [Fact]
    public async Task A_promotion_needs_a_closed_source_year_D79()
    {
        var api = new FakePromotionApi { SourceClosed = false };

        Assert.Equal(ProblemCodes.year_not_closed, (await Fails(() => api.CreateRunAsync(Create()))).Code);
    }

    [Fact]
    public async Task The_preview_is_paged_filtered_and_suggests_from_the_level_mapping_only_D83()
    {
        var api = new FakePromotionApi(30);
        var run = await api.CreateRunAsync(Create());

        var page = await api.GetRowsAsync(run.Id, new PromotionRowsQuery(Page: 2, PageSize: 10));
        var graduates = await api.GetRowsAsync(run.Id, new PromotionRowsQuery(PromotionOutcomes.Graduate, PageSize: 50));
        var searched = await api.GetRowsAsync(run.Id, new PromotionRowsQuery(Search: "Student 7"));

        Assert.Equal(30, page.Total);
        Assert.Equal(10, page.Items.Count);
        Assert.Equal(10, graduates.Total); // the final level graduates, the others promote
        Assert.All(graduates.Items, r => Assert.Equal(r.SuggestedOutcome, r.Outcome));
        Assert.Contains(searched.Items, r => r.NameEn == "Student 7");
    }

    [Fact]
    public async Task Decisions_are_saved_as_a_draft_at_most_200_per_call()
    {
        var api = new FakePromotionApi(250);
        var run = await api.CreateRunAsync(Create());
        var rows = (await api.GetRowsAsync(run.Id, new PromotionRowsQuery(PageSize: 200))).Items;

        var tooMany = rows.Select(r => new PromotionDecisionDto(r.StudentId, PromotionOutcomes.Repeat, null)).Append(new PromotionDecisionDto(Guid.NewGuid(), PromotionOutcomes.Repeat, null)).ToList();
        Assert.Equal(400, (await Fails(() => api.SaveDecisionsAsync(run.Id, new SavePromotionDecisionsRequest(tooMany)))).Status);

        await api.SaveDecisionsAsync(run.Id, new SavePromotionDecisionsRequest([new PromotionDecisionDto(rows[0].StudentId, PromotionOutcomes.Repeat, null)]));

        var reloaded = await api.GetRowsAsync(run.Id, new PromotionRowsQuery(PromotionOutcomes.Repeat));
        Assert.Equal([rows[0].StudentId], reloaded.Items.Select(r => r.StudentId)); // kept on the server, survives a reload
    }

    [Fact]
    public async Task A_bulk_change_takes_a_filter_and_null_resets_to_the_suggestion()
    {
        var api = new FakePromotionApi(30);
        var run = await api.CreateRunAsync(Create());

        var counts = await api.SetOutcomeForFilterAsync(run.Id, new SetPromotionOutcomeForFilterRequest(new PromotionRowsQuery(PromotionOutcomes.Graduate), PromotionOutcomes.Repeat, null));
        Assert.Equal(new PromotionCountsDto(20, 10, 0, 0, 0), counts);

        var reset = await api.SetOutcomeForFilterAsync(run.Id, new SetPromotionOutcomeForFilterRequest(new PromotionRowsQuery(), null, null));
        Assert.Equal(new PromotionCountsDto(20, 0, 10, 0, 0), reset);
    }

    [Fact]
    public async Task Execute_needs_the_typed_count_and_a_missing_level_mapping_blocks_it()
    {
        var api = new FakePromotionApi(12) { Now = () => T0 };
        var run = await api.CreateRunAsync(Create());

        Assert.Equal(nameof(ExecutePromotionRequest.ConfirmStudentCount), (await Fails(() => api.ExecuteAsync(run.Id, new ExecutePromotionRequest(11)))).Field);
        api.LevelMappingMissing = true;
        Assert.Equal(ProblemCodes.promotion_conflict, (await Fails(() => api.ExecuteAsync(run.Id, new ExecutePromotionRequest(12)))).Code);
        Assert.Contains((await api.GetRunAsync(run.Id)).Blocking, b => b.Code == YearEndIssueCodes.NoLevelMapping);

        api.LevelMappingMissing = false;
        var done = await api.ExecuteAsync(run.Id, new ExecutePromotionRequest(12));

        Assert.Equal(PromotionRunStates.Done, done.State);
        Assert.Equal(12, done.Processed);
        Assert.Equal(T0.AddMinutes(10), done.UndoUntilUtc); // D12: ten minutes by default
    }

    [Fact]
    public async Task A_failure_for_one_student_does_not_stop_the_rest_and_is_listed_D84()
    {
        var api = new FakePromotionApi(10) { Now = () => T0 };
        api.FailStudents.Add(3);
        var run = await api.CreateRunAsync(Create());

        var done = await api.ExecuteAsync(run.Id, new ExecutePromotionRequest(10));
        var failures = await api.GetFailuresAsync(run.Id);

        Assert.Equal(PromotionRunStates.PartiallyFailed, done.State);
        Assert.Equal(1, done.Failed);
        Assert.Equal(10, done.Processed);
        Assert.Single(failures.Items);
        Assert.Equal("S00003", failures.Items[0].StudentCode);
    }

    [Fact]
    public async Task A_slow_run_stays_running_until_it_is_advanced_and_a_second_run_is_refused()
    {
        var api = new FakePromotionApi(10) { Slow = true };
        var run = await api.CreateRunAsync(Create());
        var started = await api.ExecuteAsync(run.Id, new ExecutePromotionRequest(10));
        Assert.Equal(PromotionRunStates.Running, started.State);
        Assert.Null(started.UndoUntilUtc);

        Assert.Equal(ProblemCodes.promotion_in_progress, (await Fails(() => api.CreateRunAsync(Create()))).Code);

        api.Advance(run.Id, 4);
        Assert.Equal(4, (await api.GetRunAsync(run.Id)).Processed);
        api.Advance(run.Id, 6);
        Assert.Equal(PromotionRunStates.Done, (await api.GetRunAsync(run.Id)).State);
    }

    [Fact]
    public async Task Undo_works_inside_the_window_and_restores_the_run()
    {
        var clock = T0;
        var api = new FakePromotionApi(5) { Now = () => clock };
        var run = await api.CreateRunAsync(Create());
        await api.ExecuteAsync(run.Id, new ExecutePromotionRequest(5));
        clock = T0.AddMinutes(9);

        var undone = await api.UndoAsync(run.Id);

        Assert.Equal(PromotionRunStates.Undone, undone.State);
        Assert.Null(undone.UndoUntilUtc);
    }

    [Fact]
    public async Task Undo_is_refused_after_the_window_by_the_server_clock_and_after_activity_D12()
    {
        var clock = T0;
        var api = new FakePromotionApi(5) { Now = () => clock };
        var run = await api.CreateRunAsync(Create());
        await api.ExecuteAsync(run.Id, new ExecutePromotionRequest(5));

        api.ActivityInNewYear = true;
        Assert.Equal(ProblemCodes.promotion_undo_activity_exists, (await Fails(() => api.UndoAsync(run.Id))).Code);
        api.ActivityInNewYear = false;
        clock = T0.AddMinutes(10).AddSeconds(1);
        Assert.Equal(ProblemCodes.promotion_undo_window_over, (await Fails(() => api.UndoAsync(run.Id))).Code);
    }

    [Fact]
    public async Task The_history_lists_runs_newest_first_and_a_finished_run_cannot_be_edited()
    {
        var api = new FakePromotionApi(5);
        var run = await api.CreateRunAsync(Create());
        await api.ExecuteAsync(run.Id, new ExecutePromotionRequest(5));

        Assert.Single((await api.ListRunsAsync()).Items);
        Assert.Equal(ProblemCodes.promotion_conflict, (await Fails(() => api.SaveDecisionsAsync(run.Id, new SavePromotionDecisionsRequest([])))).Code);
    }

    [Fact]
    public async Task Undo_puts_back_only_the_students_that_were_moved_and_keeps_the_failures_D84()
    {
        var api = new FakePromotionApi(10) { Now = () => T0 };
        api.FailStudents.Add(3);
        api.FailStudents.Add(7);
        var run = await api.CreateRunAsync(Create());
        await api.ExecuteAsync(run.Id, new ExecutePromotionRequest(10));

        var undone = await api.UndoAsync(run.Id);

        Assert.Equal(PromotionRunStates.Undone, undone.State);
        Assert.Equal(8, api.RestoredCount(run.Id)); // the eight that moved are back
        Assert.Equal(2, undone.Failed); // the two that never moved stay listed for a fix and a new run
        var failures = await api.GetFailuresAsync(run.Id);
        Assert.Equal(["S00003", "S00007"], failures.Items.Select(f => f.StudentCode).Order());
    }

    // --- lifecycle

    private static WithdrawStudentRequest Withdraw(string reason = "MovedAway", string? note = null, DateOnly? on = null) =>
        new(reason, note, on ?? DateOnly.FromDateTime(T0));

    [Fact]
    public async Task Withdrawal_removes_the_student_from_active_lists_blocks_sign_in_and_fills_the_archive()
    {
        var api = new FakeLifecycleApi { Now = () => T0 };
        var id = FakeLifecycleApi.ActiveStudentId;
        Assert.True(api.CanSignIn(id));

        await api.WithdrawAsync(id, Withdraw());

        Assert.False(api.IsActive(id));
        Assert.False(api.CanSignIn(id));
        var archive = (await api.GetArchiveAsync(new ArchiveQuery())).Items;
        Assert.Contains(archive, a => a.StudentId == id && a.ExitKind == StudentExitKinds.Withdrawn && a.ReasonKey == "MovedAway");
        Assert.Equal(ProblemCodes.student_already_inactive, (await Fails(() => api.WithdrawAsync(id, Withdraw()))).Code);
    }

    [Fact]
    public async Task Other_needs_a_note_an_unknown_reason_is_refused_and_a_far_date_is_invalid()
    {
        var api = new FakeLifecycleApi { Now = () => T0 };
        var id = FakeLifecycleApi.ActiveStudentId;

        Assert.Equal(nameof(WithdrawStudentRequest.Note), (await Fails(() => api.WithdrawAsync(id, Withdraw(WithdrawalReasons.Other)))).Field);
        Assert.Equal(nameof(WithdrawStudentRequest.ReasonKey), (await Fails(() => api.WithdrawAsync(id, Withdraw("Nope")))).Field);
        Assert.Equal(ProblemCodes.effective_date_invalid, (await Fails(() => api.WithdrawAsync(id, Withdraw(on: new DateOnly(2030, 1, 1))))).Code);

        await api.WithdrawAsync(id, Withdraw(WithdrawalReasons.Other, "family decision"));
        Assert.True(api.IsActive(id) == false);
    }

    [Fact]
    public async Task Graduation_needs_no_reason_and_the_archive_filters_by_kind_and_search()
    {
        var api = new FakeLifecycleApi { Now = () => T0 };
        var other = api.ActiveStudents.First(i => i != FakeLifecycleApi.ActiveStudentId);
        await api.WithdrawAsync(FakeLifecycleApi.ActiveStudentId, Withdraw());
        await api.GraduateAsync(other, new GraduateStudentRequest(DateOnly.FromDateTime(T0)));

        Assert.Single((await api.GetArchiveAsync(new ArchiveQuery(ExitKind: StudentExitKinds.Graduated))).Items);
        Assert.Single((await api.GetArchiveAsync(new ArchiveQuery(Search: "Sara"))).Items);
        Assert.Equal(2, (await api.GetArchiveAsync(new ArchiveQuery())).Total);
    }

    [Fact]
    public async Task Readmission_keeps_the_code_restores_sign_in_and_refuses_a_full_class_or_a_closed_year()
    {
        var api = new FakeLifecycleApi { Now = () => T0 };
        var id = FakeLifecycleApi.ActiveStudentId;
        var code = api.CodeOf(id);
        await api.WithdrawAsync(id, Withdraw());

        Assert.Equal(ProblemCodes.readmit_class_full, (await Fails(() => api.ReadmitAsync(id, new ReadmitStudentRequest(FakeLifecycleApi.FullClassId, DateOnly.FromDateTime(T0), null)))).Code);
        Assert.Equal(ProblemCodes.readmit_year_closed, (await Fails(() => api.ReadmitAsync(id, new ReadmitStudentRequest(FakeLifecycleApi.ClosedYearClassId, DateOnly.FromDateTime(T0), null)))).Code);
        await api.ReadmitAsync(id, new ReadmitStudentRequest(FakeLifecycleApi.OpenClassId, DateOnly.FromDateTime(T0), null));

        Assert.True(api.CanSignIn(id));
        Assert.Equal(code, api.CodeOf(id)); // D25: the code is never reused or changed
        Assert.Empty((await api.GetArchiveAsync(new ArchiveQuery())).Items);
    }

    [Fact]
    public async Task The_reason_list_is_client_managed_and_Other_is_never_part_of_it_D82()
    {
        var api = new FakeLifecycleApi();

        Assert.NotEmpty(await api.GetReasonsAsync());
        await api.SaveReasonsAsync(new SaveWithdrawalReasonsRequest([new WithdrawalReasonDto("Health", "ظروف صحية", "Health reasons", true)]));
        Assert.Equal(["Health"], (await api.GetReasonsAsync()).Select(r => r.Key));
        Assert.Equal(400, (await Fails(() => api.SaveReasonsAsync(new SaveWithdrawalReasonsRequest([new WithdrawalReasonDto(WithdrawalReasons.Other, "أخرى", "Other", true)])))).Status);
    }

    // --- roles (D82)

    [Theory]
    [InlineData("OrgAdmin", true, true, true)]
    [InlineData("Manager", true, true, true)]
    [InlineData("Counselor", true, false, true)] // a Counselor may withdraw and read the archive, never re-admit
    [InlineData("Supervisor", false, false, true)]
    [InlineData("Teacher", false, false, false)]
    [InlineData("Student", false, false, false)]
    public async Task The_fake_holds_what_the_role_holds(string role, bool withdraw, bool readmit, bool archive)
    {
        var api = new FakeLifecycleApi { Now = () => T0, Role = role };
        var other = api.ActiveStudents.First(i => i != FakeLifecycleApi.ActiveStudentId);

        async Task<int?> Status(Func<Task> call)
        {
            try { await call(); return null; }
            catch (ApiProblemException ex) { return ex.Status; }
        }

        Assert.Equal(withdraw ? null : 403, await Status(() => api.WithdrawAsync(FakeLifecycleApi.ActiveStudentId, Withdraw())));
        Assert.Equal(withdraw ? null : 403, await Status(() => api.GraduateAsync(other, new GraduateStudentRequest(DateOnly.FromDateTime(T0)))));
        Assert.Equal(archive ? null : 403, await Status(() => api.GetArchiveAsync(new ArchiveQuery())));

        var admin = new FakeLifecycleApi { Now = () => T0 }; // somebody left, so there is something to re-admit
        await admin.WithdrawAsync(FakeLifecycleApi.ActiveStudentId, Withdraw());
        admin.Role = role;
        var result = await Status(() => admin.ReadmitAsync(FakeLifecycleApi.ActiveStudentId, new ReadmitStudentRequest(FakeLifecycleApi.OpenClassId, DateOnly.FromDateTime(T0), null)));
        Assert.Equal(readmit ? null : 403, result);
    }

    // --- open the next year

    private static OpenNextYearRequest Next(Guid? copyFrom = null, string[]? parts = null, Guid? template = null, DateOnly? start = null) =>
        new("٢٠٢٧", "2027/2028", start ?? new DateOnly(2027, 9, 1), new DateOnly(2028, 6, 30), copyFrom, parts ?? [], [], template, null);

    [Fact]
    public async Task Opening_a_year_needs_something_to_copy_or_a_template()
    {
        var api = new FakeNewYearApi();

        Assert.Equal(ProblemCodes.copy_nothing_selected, (await Fails(() => api.PreviewAsync(Year, Next()))).Code);
        Assert.Equal(ProblemCodes.copy_nothing_selected, (await Fails(() => api.StartAsync(Year, Next(Year, [])))).Code);
    }

    [Fact]
    public async Task Dates_must_be_ordered_and_must_not_overlap_another_year()
    {
        var api = new FakeNewYearApi();

        Assert.Equal(ProblemCodes.year_overlap, (await Fails(() => api.PreviewAsync(Year, Next(Year, [YearCopyParts.Classes], start: new DateOnly(2026, 12, 1))))).Code);
        var backwards = Next(Year, [YearCopyParts.Classes]) with { EndDate = new DateOnly(2027, 1, 1) };
        Assert.Equal(nameof(OpenNextYearRequest.EndDate), (await Fails(() => api.PreviewAsync(Year, backwards))).Field);
    }

    [Fact]
    public async Task The_preview_counts_what_is_created_and_lists_what_a_template_skips()
    {
        var api = new FakeNewYearApi();

        var copy = await api.PreviewAsync(Year, Next(Year, [YearCopyParts.Classes, YearCopyParts.ClassCourses]));
        var template = await api.PreviewAsync(Year, Next(template: Guid.NewGuid()));

        Assert.Equal((8, 40, 0), (copy.Classes, copy.ClassCourses, copy.Periods));
        Assert.Empty(copy.Skipped);
        Assert.Equal(12, template.UnitsFromTemplate);
        Assert.NotEmpty(template.Skipped);
    }

    [Fact]
    public async Task The_copy_is_a_job_that_ends_with_a_new_planned_year()
    {
        var api = new FakeNewYearApi { Slow = true };
        var started = await api.StartAsync(Year, Next(Year, [YearCopyParts.Classes]));
        Assert.Equal(NewYearRunStates.Running, started.State);
        Assert.Null(started.NewYearId);

        api.Advance(started.Id, 3);
        var mid = await api.GetRunAsync(Year, started.Id);
        Assert.Equal(3, mid.Processed);

        api.Advance(started.Id, 100);
        var done = await api.GetRunAsync(Year, started.Id);
        Assert.Equal(NewYearRunStates.Done, done.State);
        Assert.NotNull(done.NewYearId);
    }

    // --- query strings of the HTTP clients

    [Fact]
    public void Query_strings_carry_only_what_is_set_and_escape_values()
    {
        Assert.Equal("?page=1&pageSize=50", YearEndQuery.Rows(new PromotionRowsQuery()));
        var id = Guid.NewGuid();
        Assert.Equal($"?outcome=Repeat&classId={id}&problemsOnly=true&search=a%20b&page=2&pageSize=20", YearEndQuery.Rows(new PromotionRowsQuery("Repeat", id, true, "a b", 2, 20)));
        Assert.Equal("?state=Pending&page=1&pageSize=50", YearEndQuery.Changes(new GradeChangeQuery("Pending")));
        Assert.Equal("?search=%D8%B3%D8%A7%D8%B1%D8%A9&exitKind=Withdrawn&page=1&pageSize=50", YearEndQuery.Archive(new ArchiveQuery("سارة", "Withdrawn")));
    }
}
