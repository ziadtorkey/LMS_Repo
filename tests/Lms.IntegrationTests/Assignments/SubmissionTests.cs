using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Assignments;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Assignments;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Assignments.AssignmentTestKit;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.Assignments;

/// <summary>M4a Task 8 (#358, FR-ASG-02 submission part): late policy on the server clock, resubmission rule, one live row, files.</summary>
[Collection(ApiCollection.Name)]
public sealed class SubmissionTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private static async Task<SubmissionDto> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SubmissionDto>())!;
    }

    [Fact]
    public async Task Late_flag_uses_server_clock()
    {
        var w = await _t.SetUpAsync("SB1");
        var onTime = await CreateAsync(_t, w.Teacher, w, Request("OnTime", due: DateTime.UtcNow.AddDays(1), latePolicy: LatePolicies.Allow));
        var late = await CreateAsync(_t, w.Teacher, w, Request("Late", due: DateTime.UtcNow.AddMinutes(-5), latePolicy: LatePolicies.Allow));
        var penalty = await CreateAsync(_t, w.Teacher, w, Request("Penalty", due: DateTime.UtcNow.AddMinutes(-5), latePolicy: LatePolicies.Penalty, penalty: 25));

        var first = await ReadAsync(await SubmitAsync(_t, w.Student, onTime.Id));
        var second = await ReadAsync(await SubmitAsync(_t, w.Student, late.Id));
        var third = await ReadAsync(await SubmitAsync(_t, w.Student, penalty.Id));

        Assert.False(first.IsLate);
        Assert.Equal(SubmissionStatuses.Submitted, first.Status);
        Assert.True(second.IsLate);
        Assert.Equal(SubmissionStatuses.Late, second.Status);
        Assert.True(third.IsLate);
        Assert.Equal(1, first.Number);
        Assert.NotNull(first.SubmittedAtUtc);
    }

    [Fact]
    public async Task Block_policy_refuses_after_due()
    {
        var w = await _t.SetUpAsync("SB2");
        var blocked = await CreateAsync(_t, w.Teacher, w, Request("Blocked", due: DateTime.UtcNow.AddMinutes(-1), latePolicy: LatePolicies.Block));

        var response = await SubmitAsync(_t, w.Student, blocked.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.submission_closed, await CodeAsync(response));
        var mine = await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.CourseAssignmentMySubmission(blocked.Id))).Content.ReadFromJsonAsync<MySubmissionDto>();
        Assert.False(mine!.CanSubmit);
        Assert.Equal(ProblemCodes.submission_closed, mine.CannotSubmitCode);
        Assert.Null(mine.Submission);
    }

    [Fact]
    public async Task Resubmission_rule_enforced()
    {
        var w = await _t.SetUpAsync("SB3");
        var none = await CreateAsync(_t, w.Teacher, w, Request("None", resubmission: ResubmissionRules.None));
        var untilDue = await CreateAsync(_t, w.Teacher, w, Request("UntilDue", due: DateTime.UtcNow.AddDays(1), resubmission: ResubmissionRules.UntilDue));
        var untilGraded = await CreateAsync(_t, w.Teacher, w, Request("UntilGraded", resubmission: ResubmissionRules.UntilGraded));

        await ReadAsync(await SubmitAsync(_t, w.Student, none.Id));
        var refused = await SubmitAsync(_t, w.Student, none.Id, "<p>again</p>");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(ProblemCodes.resubmission_not_allowed, await CodeAsync(refused));

        await ReadAsync(await SubmitAsync(_t, w.Student, untilDue.Id, "<p>one</p>"));
        var again = await ReadAsync(await SubmitAsync(_t, w.Student, untilDue.Id, "<p>two</p>"));
        Assert.Equal(2, again.Number);
        Assert.Contains("two", again.TextHtml!);

        await ReadAsync(await SubmitAsync(_t, w.Student, untilGraded.Id));
        await SetStatusAsync(untilGraded.Id, w.StudentProfileId, SubmissionStatuses.Graded);
        Assert.Equal(HttpStatusCode.Conflict, (await SubmitAsync(_t, w.Student, untilGraded.Id, "<p>late edit</p>")).StatusCode);
        await SetStatusAsync(untilGraded.Id, w.StudentProfileId, SubmissionStatuses.Returned);
        Assert.Equal(3 - 1, (await ReadAsync(await SubmitAsync(_t, w.Student, untilGraded.Id, "<p>after return</p>"))).Number);
    }

    [Fact]
    public async Task Resubmission_after_due_under_UntilDue_is_refused()
    {
        var w = await _t.SetUpAsync("SB4");
        var a = await CreateAsync(_t, w.Teacher, w, Request("Past", due: DateTime.UtcNow.AddMinutes(-1), resubmission: ResubmissionRules.UntilDue));

        await ReadAsync(await SubmitAsync(_t, w.Student, a.Id));
        var again = await SubmitAsync(_t, w.Student, a.Id, "<p>again</p>");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(ProblemCodes.resubmission_not_allowed, await CodeAsync(again));
    }

    [Fact]
    public async Task Concurrent_submits_create_one_submission()
    {
        var w = await _t.SetUpAsync("SB5");
        var a = await CreateAsync(_t, w.Teacher, w, Request("Race", resubmission: ResubmissionRules.UntilGraded));

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => SubmitAsync(_t, w.Student, a.Id, $"<p>try {i}</p>")));

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, r.StatusCode.ToString()));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await db.Set<Submission>().CountAsync(s => s.AssignmentId == a.Id && s.StudentId == w.StudentProfileId));
    }

    [Fact]
    public async Task Submit_completes_submit_rule_module()
    {
        var w = await _t.SetUpAsync("SB6");
        var a = await CreateAsync(_t, w.Teacher, w, Request("Done", rule: CompletionRules.Submit));
        var before = await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.ModuleCompletion(a.Module.Id))).Content.ReadFromJsonAsync<ModuleCompletionDto>();
        Assert.NotEqual(CompletionStates.Completed, before!.State);

        await ReadAsync(await SubmitAsync(_t, w.Student, a.Id, draft: true));
        var draft = await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.ModuleCompletion(a.Module.Id))).Content.ReadFromJsonAsync<ModuleCompletionDto>();
        Assert.NotEqual(CompletionStates.Completed, draft!.State);

        await ReadAsync(await SubmitAsync(_t, w.Student, a.Id));
        var after = await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.ModuleCompletion(a.Module.Id))).Content.ReadFromJsonAsync<ModuleCompletionDto>();
        Assert.Equal(CompletionStates.Completed, after!.State);
    }

    [Fact]
    public async Task Content_rules_and_text_sanitising()
    {
        var w = await _t.SetUpAsync("SB7");
        var textOnly = await CreateAsync(_t, w.Teacher, w, Request("TextOnly", allowFiles: false));
        var filesOnly = await CreateAsync(_t, w.Teacher, w, Request("FilesOnly", allowText: false));
        var file = await AssetOfAsync(factory, w, await UserOfAsync(w.StudentProfileId));

        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitAsync(_t, w.Student, textOnly.Id, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitAsync(_t, w.Student, textOnly.Id, "<p>x</p>", [file])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitAsync(_t, w.Student, filesOnly.Id, "<p>x</p>")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitAsync(_t, w.Student, filesOnly.Id, null, [Guid.NewGuid()])).StatusCode);
        var clean = await ReadAsync(await SubmitAsync(_t, w.Student, textOnly.Id, "<p>ok<script>alert(1)</script></p>"));
        Assert.DoesNotContain("script", clean.TextHtml!, StringComparison.OrdinalIgnoreCase);
        var withFile = await ReadAsync(await SubmitAsync(_t, w.Student, filesOnly.Id, null, [file]));
        Assert.Equal(file, Assert.Single(withFile.Files).Id);
    }

    [Fact]
    public async Task A_student_cannot_attach_a_file_somebody_else_uploaded()
    {
        var w = await _t.SetUpAsync("SB8");
        var a = await CreateAsync(_t, w.Teacher, w, Request("Files"));
        var teachersFile = await AssetOfAsync(factory, w, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitAsync(_t, w.Student, a.Id, null, [teachersFile])).StatusCode);
    }

    [Fact]
    public async Task Student_cannot_read_other_students_submission_file()
    {
        var w = await _t.SetUpAsync("SB9");
        var other = await _t.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);
        var attachment = await AssetOfAsync(factory, w, w.Teacher.UserId);
        var a = await CreateAsync(_t, w.Teacher, w, Request("Files", attachments: [attachment]));
        var file = await AssetOfAsync(factory, w, await UserOfAsync(w.StudentProfileId));
        await ReadAsync(await SubmitAsync(_t, w.Student, a.Id, null, [file]));

        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.AssetUrl(file))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(other.Student, HttpMethod.Get, Routes.AssetUrl(file))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(other.Student, HttpMethod.Get, Routes.Asset(file))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.AssetUrl(file))).StatusCode);
        // The attachment of a visible assignment is readable by every enrolled student; the stranger gets nothing.
        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(other.Student, HttpMethod.Get, Routes.AssetUrl(attachment))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Stranger, HttpMethod.Get, Routes.AssetUrl(attachment))).StatusCode);
    }

    [Fact]
    public async Task Staff_list_shows_every_enrolled_student_and_hides_drafts()
    {
        var w = await _t.SetUpAsync("SB10");
        var other = await _t.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);
        var third = await _t.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);
        var a = await CreateAsync(_t, w.Teacher, w, Request("List", due: DateTime.UtcNow.AddMinutes(-1)));
        await ReadAsync(await SubmitAsync(_t, w.Student, a.Id));
        await ReadAsync(await SubmitAsync(_t, other.Student, a.Id, draft: true));

        var all = await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.CourseAssignmentSubmissions(a.Id)))
            .Content.ReadFromJsonAsync<PagedResult<SubmissionListItemDto>>();
        var none = await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.CourseAssignmentSubmissions(a.Id) + "?status=None"))
            .Content.ReadFromJsonAsync<PagedResult<SubmissionListItemDto>>();
        var late = await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.CourseAssignmentSubmissions(a.Id) + "?isLate=true"))
            .Content.ReadFromJsonAsync<PagedResult<SubmissionListItemDto>>();

        Assert.Equal(3, all!.Total);
        Assert.Equal(1, all.Items.Count(i => i.SubmissionId is not null));
        Assert.Equal([other.ProfileId, third.ProfileId], none!.Items.Select(i => i.StudentId).Order().ToArray().Intersect([other.ProfileId, third.ProfileId]).Order().ToArray());
        Assert.Equal(2, none.Total);
        Assert.Equal([w.StudentProfileId], late!.Items.Select(i => i.StudentId).ToArray());
        var counts = await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.CourseAssignment(a.Id))).Content.ReadFromJsonAsync<CourseAssignmentDto>();
        Assert.Equal(1, counts!.SubmittedCount);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Get, Routes.CourseAssignmentSubmissions(a.Id))).StatusCode);
    }

    [Fact]
    public async Task Submission_is_readable_by_its_owner_and_staff_only()
    {
        var w = await _t.SetUpAsync("SB11");
        var other = await _t.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);
        var a = await CreateAsync(_t, w.Teacher, w, Request("Own"));
        var mine = await ReadAsync(await SubmitAsync(_t, w.Student, a.Id));

        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.Submission(mine.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.Submission(mine.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(other.Student, HttpMethod.Get, Routes.Submission(mine.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Get, Routes.Submission(mine.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.Submission(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task A_closed_class_blocks_submissions_with_409()
    {
        var w = await _t.SetUpAsync("SB12");
        var a = await CreateAsync(_t, w.Teacher, w, Request("Closing"));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var cls = await db.Set<Lms.Domain.Classes.Class>().SingleAsync(c => c.Id == w.Class.Id);
            cls.SetStatus(Lms.Domain.Classes.Class.ClosedStatus);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await SubmitAsync(_t, w.Student, a.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.CourseAssignment(a.Id))).StatusCode);
    }

    private async Task<Guid> UserOfAsync(Guid studentProfileId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return (await db.Set<Lms.Domain.People.StudentProfile>().SingleAsync(s => s.Id == studentProfileId)).UserId!.Value;
    }

    /// <summary>Moves the submission to a grading status directly (the grading endpoints are Task 9).</summary>
    private async Task SetStatusAsync(Guid assignmentId, Guid studentId, string status)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var row = await db.Set<Submission>().SingleAsync(s => s.AssignmentId == assignmentId && s.StudentId == studentId);
        db.Entry(row).Property(nameof(Submission.Status)).CurrentValue = status;
        await db.SaveChangesAsync();
    }
}
