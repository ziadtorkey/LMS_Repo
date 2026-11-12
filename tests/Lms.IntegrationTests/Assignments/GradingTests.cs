using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.Domain.Assignments;
using Lms.Domain.Content;
using Lms.Domain.Grading;
using Lms.Domain.Notifications;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Assignments;
using Lms.Shared.Content;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Assignments.AssignmentTestKit;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.Assignments;

/// <summary>M4a Task 9 (#359, FR-ASG-02 grading part): grade, penalty, feedback, return, grade events, completion and notice.</summary>
[Collection(ApiCollection.Name)]
public sealed class GradingTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private async Task<(ContentWorld W, CourseAssignmentDto A, SubmissionDto S)> SubmittedAsync(
        string name, CreateCourseAssignmentRequest? request = null)
    {
        var w = await _t.SetUpAsync(name);
        var a = await CreateAsync(_t, w.Teacher, w, request ?? Request(due: DateTime.UtcNow.AddDays(1)));
        var response = await SubmitAsync(_t, w.Student, a.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (w, a, (await response.Content.ReadFromJsonAsync<SubmissionDto>())!);
    }

    private Task<HttpResponseMessage> GradeAsync(RoleAssignment caller, Guid submissionId, decimal grade, string? feedback = null, Guid? annotated = null) =>
        _t.SendAsync(caller, HttpMethod.Put, Routes.SubmissionGrade(submissionId), new GradeSubmissionRequest(grade, feedback, annotated));

    private Task<HttpResponseMessage> ReturnAsync(RoleAssignment caller, Guid submissionId) =>
        _t.SendAsync(caller, HttpMethod.Post, Routes.SubmissionReturn(submissionId));

    private async Task<List<GradeEvent>> EventsAsync(Guid assignmentId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<GradeEvent>().Where(e => e.SourceId == assignmentId).OrderBy(e => e.Revision).ToListAsync();
    }

    private static async Task<SubmissionDto> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SubmissionDto>())!;
    }

    [Theory]
    [InlineData("10.01")]
    [InlineData("-1")]
    [InlineData("5.123")]
    public async Task Grade_above_max_or_negative_or_too_precise_is_400(string grade)
    {
        var (w, _, s) = await SubmittedAsync("GR1");

        var response = await GradeAsync(w.Teacher, s.Id, decimal.Parse(grade, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.validation_failed, await CodeAsync(response));
    }

    [Fact]
    public async Task Grade_saves_without_releasing_and_max_grade_is_allowed()
    {
        var (w, _, s) = await SubmittedAsync("GR2");

        var graded = await ReadAsync(await GradeAsync(w.Teacher, s.Id, 10, "<p>Great</p>"));

        Assert.Equal(SubmissionStatuses.Graded, graded.Status);
        Assert.Equal(10, graded.Grade?.FinalGrade);
        Assert.Null(graded.Grade?.ReturnedAtUtc);
    }

    [Fact]
    public async Task Penalty_applied_on_late_submission()
    {
        var (w, _, s) = await SubmittedAsync(
            "GR3", Request("Late", due: DateTime.UtcNow.AddMinutes(-5), latePolicy: LatePolicies.Penalty, penalty: 25));
        Assert.True(s.IsLate);

        var graded = await ReadAsync(await GradeAsync(w.Teacher, s.Id, 8));

        Assert.Equal(8, graded.Grade?.Grade);
        Assert.Equal(25, graded.Grade?.PenaltyPercentApplied);
        Assert.Equal(6, graded.Grade?.FinalGrade);
    }

    [Fact]
    public async Task No_penalty_when_on_time_or_policy_is_allow()
    {
        var (w, _, onTime) = await SubmittedAsync("GR4", Request(due: DateTime.UtcNow.AddDays(1), latePolicy: LatePolicies.Penalty, penalty: 25));
        var (w2, _, late) = await SubmittedAsync("GR4B", Request(due: DateTime.UtcNow.AddMinutes(-5), latePolicy: LatePolicies.Allow));

        var first = await ReadAsync(await GradeAsync(w.Teacher, onTime.Id, 8));
        var second = await ReadAsync(await GradeAsync(w2.Teacher, late.Id, 8));

        Assert.Equal((null, 8m), (first.Grade?.PenaltyPercentApplied, first.Grade?.FinalGrade));
        Assert.Equal((null, 8m), (second.Grade?.PenaltyPercentApplied, second.Grade?.FinalGrade));
    }

    [Fact]
    public async Task Grading_writes_grade_event_once_per_revision()
    {
        var (w, a, s) = await SubmittedAsync("GR5");
        await GradeAsync(w.Teacher, s.Id, 7);
        Assert.Empty(await EventsAsync(a.Id)); // not released yet

        Assert.Equal(HttpStatusCode.OK, (await ReturnAsync(w.Teacher, s.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ReturnAsync(w.Teacher, s.Id)).StatusCode); // returning again changes nothing
        var first = Assert.Single(await EventsAsync(a.Id));
        Assert.Equal((1, 7m, 10m, GradeSourceKind.Assignment), (first.Revision, first.Points, first.MaxPoints, first.SourceKind));
        Assert.Equal(s.StudentId, first.StudentProfileId);

        await GradeAsync(w.Teacher, s.Id, 7); // same grade saved and returned again
        await ReturnAsync(w.Teacher, s.Id);
        Assert.Single(await EventsAsync(a.Id));

        await GradeAsync(w.Teacher, s.Id, 9); // a changed grade returned again is the next revision
        await ReturnAsync(w.Teacher, s.Id);
        var events = await EventsAsync(a.Id);
        Assert.Equal([1, 2], events.Select(e => e.Revision).ToArray());
        Assert.Equal(9, events[1].Points);
    }

    [Fact]
    public async Task Student_sees_feedback_only_after_return()
    {
        var (w, a, s) = await SubmittedAsync("GR6");
        var annotated = await AssetOfAsync(factory, w, w.Teacher.UserId);
        await GradeAsync(w.Teacher, s.Id, 8, "<p>Well done</p>", annotated);

        var before = await ReadAsync(await _t.SendAsync(w.Student, HttpMethod.Get, Routes.Submission(s.Id)));
        var mineBefore = (await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.CourseAssignmentMySubmission(a.Id))).Content
            .ReadFromJsonAsync<MySubmissionDto>())!;
        Assert.Null(before.Grade);
        Assert.Null(mineBefore.Submission?.Grade);

        await ReturnAsync(w.Teacher, s.Id);

        var after = await ReadAsync(await _t.SendAsync(w.Student, HttpMethod.Get, Routes.Submission(s.Id)));
        Assert.Equal(SubmissionStatuses.Returned, after.Status);
        Assert.Equal(8, after.Grade?.FinalGrade);
        Assert.Equal("<p>Well done</p>", after.Grade?.FeedbackHtml);
        Assert.Equal(annotated, after.Grade?.AnnotatedFile?.Id);
        Assert.NotNull(after.Grade?.ReturnedAtUtc);

        // A regrade that is saved but not returned yet hides the grade again.
        await GradeAsync(w.Teacher, s.Id, 3);
        Assert.Null((await ReadAsync(await _t.SendAsync(w.Student, HttpMethod.Get, Routes.Submission(s.Id)))).Grade);
    }

    [Fact]
    public async Task Other_teacher_cannot_grade_and_students_are_forbidden()
    {
        var (w, _, s) = await SubmittedAsync("GR7");

        Assert.Equal(HttpStatusCode.NotFound, (await GradeAsync(w.OtherTeacher, s.Id, 5)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ReturnAsync(w.OtherTeacher, s.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GradeAsync(w.Student, s.Id, 10)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ReturnAsync(w.Student, s.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GradeAsync(w.Admin, s.Id, 5)).StatusCode);
    }

    [Fact]
    public async Task Feedback_is_sanitised_on_write()
    {
        var (w, _, s) = await SubmittedAsync("GR8");

        var graded = await ReadAsync(await GradeAsync(w.Teacher, s.Id, 5, "<p onclick=\"x()\">Good<script>alert(1)</script></p>"));

        Assert.Equal("<p>Good</p>", graded.Grade?.FeedbackHtml);
    }

    [Fact]
    public async Task Annotated_file_must_be_an_asset_of_the_class_course()
    {
        var (w, _, s) = await SubmittedAsync("GR9");

        var response = await GradeAsync(w.Teacher, s.Id, 5, null, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Return_needs_a_saved_grade_and_drafts_are_invisible_to_staff()
    {
        var (w, a, s) = await SubmittedAsync("GR10");
        var draft = await ReadAsync(await SubmitAsync(_t, w.Student, (await CreateAsync(_t, w.Teacher, w, Request("Other"))).Id, draft: true));

        var notGraded = await ReturnAsync(w.Teacher, s.Id);
        var draftGrade = await GradeAsync(w.Teacher, draft.Id, 5);

        Assert.Equal(HttpStatusCode.BadRequest, notGraded.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, draftGrade.StatusCode);
        Assert.Empty(await EventsAsync(a.Id));
    }

    [Fact]
    public async Task Return_completes_a_Score_rule_module_and_notifies_the_student_once()
    {
        var w = await _t.SetUpAsync("GR11");
        var request = Request(rule: CompletionRules.Score, due: DateTime.UtcNow.AddDays(1));
        request = request with { Module = request.Module with { CompletionMinScore = 5 } };
        var a = await CreateAsync(_t, w.Teacher, w, request);
        var s = (await (await SubmitAsync(_t, w.Student, a.Id)).Content.ReadFromJsonAsync<SubmissionDto>())!;
        await GradeAsync(w.Teacher, s.Id, 8);

        await ReturnAsync(w.Teacher, s.Id);
        await ReturnAsync(w.Teacher, s.Id);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var module = await db.Set<LessonModule>().SingleAsync(m => m.RefId == a.Id);
        var completion = await db.Set<ModuleCompletion>().SingleAsync(c => c.ModuleId == module.Id && c.StudentId == s.StudentId);
        Assert.Equal(CompletionStates.Completed, completion.State);
        var notices = await db.Set<Notification>().Where(n => n.UserId == w.Student.UserId && n.EventKey == NotificationEvents.AssignmentsGraded).ToListAsync();
        Assert.Single(notices); // returning twice releases once
    }

    [Fact]
    public async Task Annotated_file_url_opens_for_the_owner_only_after_return()
    {
        var (w, _, s) = await SubmittedAsync("GR12");
        var annotated = await AssetOfAsync(factory, w, w.Teacher.UserId);
        await GradeAsync(w.Teacher, s.Id, 8, null, annotated);

        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.AssetUrl(annotated))).StatusCode);
        await ReturnAsync(w.Teacher, s.Id);

        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.AssetUrl(annotated))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.AssetUrl(annotated))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Stranger, HttpMethod.Get, Routes.AssetUrl(annotated))).StatusCode);

        await GradeAsync(w.Teacher, s.Id, 3, null, annotated); // saved again, not returned: closed again
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.AssetUrl(annotated))).StatusCode);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) => await ContentTestHost.CodeAsync(response);
}
