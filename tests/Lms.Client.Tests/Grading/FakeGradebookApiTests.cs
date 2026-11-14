using Lms.Client.Services.Api;
using Lms.Client.Services.Grading;
using Lms.Shared;
using Lms.Shared.Grading;

namespace Lms.Client.Tests.Grading;

/// <summary>The fake gradebook mimics the rules the grading screens depend on (M5 Task 22). Its numbers are a demo engine, never the server's.</summary>
public sealed class FakeGradebookApiTests
{
    private static readonly Guid Course = FakeGradebookApi.DemoClassCourseId;
    private static readonly Guid Student = FakeGradebookApi.StudentIds[0];
    private readonly FakeGradebookApi _api = new();

    private async Task<long> VersionAsync(Guid student) =>
        (await _api.GetAsync(Course, new GradebookQuery())).Rows.Items.First(r => r.StudentId == student).Version;

    private static Task<ApiProblemException> ProblemAsync(Func<Task> call) => Assert.ThrowsAsync<ApiProblemException>(call);

    [Fact]
    public void Fake_engine_is_marked_as_demo_only() => Assert.True(FakeGradebookApi.IsDemoEngine);

    [Fact]
    public async Task Fake_auto_items_default_to_hidden()
    {
        var book = await _api.GetAsync(Course, new GradebookQuery());

        var auto = book.Items.First(i => !i.IsManual);
        Assert.Equal(PublishStates.Hidden, auto.PublishState);
        Assert.All(book.Items, i => Assert.Equal(PublishStates.Hidden, i.PublishState));
    }

    [Fact]
    public async Task Fake_returns_published_only_for_students()
    {
        var before = await _api.GetMyGradesAsync();
        Assert.Empty(before.Courses[0].Items);

        await _api.PublishAsync(Course, new PublishItemsRequest([FakeGradebookApi.ManualItemId], PublishModes.Now, null, true));
        var after = await _api.GetMyGradesAsync();

        var published = Assert.Single(after.Courses[0].Items);
        Assert.Equal(FakeGradebookApi.ManualItemId, published.ItemId);
        Assert.Equal(18m, published.Points);
        Assert.NotNull(after.Courses[0].Legend);
    }

    [Fact]
    public async Task Fake_hiding_an_item_removes_it_from_the_student_view()
    {
        await _api.PublishAsync(Course, new PublishItemsRequest([FakeGradebookApi.ManualItemId], PublishModes.Now, null, true));
        await _api.PublishAsync(Course, new PublishItemsRequest([FakeGradebookApi.ManualItemId], PublishModes.Hide, null, false));

        Assert.Empty((await _api.GetStudentViewAsync(Course, Student)).Items);
    }

    [Fact]
    public async Task Fake_publishing_under_a_provisional_scale_needs_the_acknowledgement()
    {
        var ex = await ProblemAsync(() => _api.PublishAsync(Course, new PublishItemsRequest([FakeGradebookApi.ManualItemId], PublishModes.Now, null, false)));

        Assert.Equal(ProblemCodes.scale_provisional, ex.Code);
        Assert.Equal(409, ex.Status);
    }

    [Fact]
    public async Task Fake_refuses_a_schedule_in_the_past()
    {
        var ex = await ProblemAsync(() => _api.PublishAsync(Course, new PublishItemsRequest([FakeGradebookApi.ManualItemId], PublishModes.Schedule, DateTime.UtcNow.AddHours(-1), true)));

        Assert.Equal(400, ex.Status);
    }

    [Fact]
    public async Task Fake_enforces_version_conflicts()
    {
        var version = await VersionAsync(Student);
        var change = new[] { new GradeEntryChange(FakeGradebookApi.ManualItemId, GradeEntryActions.Set, 15m, null) };

        var row = await _api.UpsertEntriesAsync(Course, new UpsertEntriesRequest(Student, version, change));
        var ex = await ProblemAsync(() => _api.UpsertEntriesAsync(Course, new UpsertEntriesRequest(Student, version, change)));

        Assert.Equal(version + 1, row.Version);
        Assert.Equal(ProblemCodes.concurrency_conflict, ex.Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public async Task Fake_refuses_an_out_of_range_score(int points)
    {
        var version = await VersionAsync(Student);

        var ex = await ProblemAsync(() => _api.UpsertEntriesAsync(Course,
            new UpsertEntriesRequest(Student, version, [new GradeEntryChange(FakeGradebookApi.ManualItemId, GradeEntryActions.Set, points, null)])));

        Assert.Equal(ProblemCodes.grade_out_of_range, ex.Code);
        Assert.Equal(422, ex.Status);
        Assert.Equal(version, await VersionAsync(Student)); // nothing was applied
    }

    [Fact]
    public async Task Fake_override_needs_a_reason()
    {
        var version = await VersionAsync(Student);

        var ex = await ProblemAsync(() => _api.UpsertEntriesAsync(Course,
            new UpsertEntriesRequest(Student, version, [new GradeEntryChange(FakeGradebookApi.AutoItemId, GradeEntryActions.Override, 5m, null)])));

        Assert.Equal(ProblemCodes.grade_override_reason_required, ex.Code);
    }

    [Fact]
    public async Task Fake_override_with_a_reason_is_kept_with_its_state()
    {
        var version = await VersionAsync(Student);

        var row = await _api.UpsertEntriesAsync(Course,
            new UpsertEntriesRequest(Student, version, [new GradeEntryChange(FakeGradebookApi.AutoItemId, GradeEntryActions.Override, 5m, "Re-marked")]));

        var cell = row.Cells.First(c => c.ItemId == FakeGradebookApi.AutoItemId);
        Assert.Equal(GradeCellStates.Override, cell.State);
        Assert.Equal("Re-marked", cell.Reason);
        Assert.Equal(5m, cell.Points);
    }

    [Fact]
    public async Task Fake_locked_gradebook_refuses_writes_and_still_reads()
    {
        _api.Locked = true;
        var version = await VersionAsync(Student);

        var ex = await ProblemAsync(() => _api.UpsertEntriesAsync(Course,
            new UpsertEntriesRequest(Student, version, [new GradeEntryChange(FakeGradebookApi.ManualItemId, GradeEntryActions.Set, 1m, null)])));
        var book = await _api.GetAsync(Course, new GradebookQuery());

        Assert.Equal(ProblemCodes.gradebook_locked, ex.Code);
        Assert.Equal(GradebookStates.Locked, book.State);
    }

    [Fact]
    public async Task Fake_category_weights_stay_in_range()
    {
        var ex = await ProblemAsync(() => _api.SaveCategoriesAsync(Course, new SaveGradeCategoriesRequest([new SaveGradeCategoryRequest(null, "أ", "A", 101m, 1)])));

        Assert.Equal(ProblemCodes.weights_invalid, ex.Code);
    }

    [Fact]
    public async Task Fake_only_manual_items_can_be_deleted()
    {
        var auto = await ProblemAsync(() => _api.DeleteItemAsync(Course, FakeGradebookApi.AutoItemId));
        await _api.DeleteItemAsync(Course, FakeGradebookApi.ManualItemId);

        Assert.Equal(409, auto.Status);
        Assert.DoesNotContain((await _api.GetAsync(Course, new GradebookQuery())).Items, i => i.Id == FakeGradebookApi.ManualItemId);
    }

    [Fact]
    public async Task Fake_pages_the_rows()
    {
        var page = (await _api.GetAsync(Course, new GradebookQuery(null, 2, 2))).Rows;

        Assert.Equal(3, page.Total);
        Assert.Single(page.Items);
        Assert.Equal(2, page.Page);
    }

    [Fact]
    public async Task Fake_a_viewer_without_manage_cannot_write_or_preview()
    {
        _api.CanManage = false;

        var write = await ProblemAsync(() => _api.CreateItemAsync(Course, new CreateGradeItemRequest(FakeGradebookApi.ExamsCategoryId, "ا", "A", 10m)));
        var preview = await ProblemAsync(() => _api.GetStudentViewAsync(Course, Student));

        Assert.Equal(403, write.Status);
        Assert.Equal(403, preview.Status);
    }

    [Fact]
    public async Task Fake_unknown_class_course_is_not_found()
    {
        var ex = await ProblemAsync(() => _api.GetAsync(Guid.NewGuid(), new GradebookQuery()));

        Assert.Equal(404, ex.Status);
    }

    [Fact]
    public void Report_query_string_carries_only_what_is_set()
    {
        var s = GradebookApi.ReportQueryString(new GradeReportQuery(StudentId: Student, TermId: FakeGradebookApi.DemoTermId, Page: 2, PageSize: 25));

        Assert.Equal($"?studentId={Student}&termId={FakeGradebookApi.DemoTermId}&page=2&pageSize=25", s);
    }
}
