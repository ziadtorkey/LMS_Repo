using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Content;
using Lms.Domain.Packages;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.Content;
using Lms.Shared.Packages;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.Packages;

/// <summary>M4b Task 9b (#478): tracking commits and finish: seq, size and rate enforced by the server; completion and grade from what it judged.</summary>
[Collection(ApiCollection.Name)]
public sealed class CommitTests(LmsApiFactory factory) : IDisposable
{
    private readonly AttemptKit _k = new(factory);

    public void Dispose() => _k.Dispose();

    private async Task<PackageAttempt> AttemptAsync(Guid id)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<PackageAttempt>().AsNoTracking().SingleAsync(a => a.Id == id);
    }

    [Fact]
    public async Task Out_of_order_commits_resolve_by_seq()
    {
        var w = await _k.SetUpAsync("PC1");
        var (package, _) = await _k.AttachedAsync(w);
        var launch = await _k.LaunchAsync(w.Student, package.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await _k.CommitAsync(w.Student, launch.AttemptId, 0, ("cmi.suspend_data", "zero"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _k.CommitAsync(w.Student, launch.AttemptId, 2, ("cmi.suspend_data", "two"), ("cmi.core.lesson_location", "p2"))).StatusCode);
        var late = await _k.CommitAsync(w.Student, launch.AttemptId, 1, ("cmi.suspend_data", "one"), ("cmi.comments", "late"));
        var repeated = await _k.CommitAsync(w.Student, launch.AttemptId, 2, ("cmi.suspend_data", "again"));

        Assert.Equal(HttpStatusCode.NoContent, late.StatusCode); // not an error
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        var attempt = await AttemptAsync(launch.AttemptId);
        Assert.Equal(2, attempt.LastSeq);
        Assert.Equal("two", attempt.Model["cmi.suspend_data"]);
        Assert.Equal("p2", attempt.Model["cmi.core.lesson_location"]);
        Assert.DoesNotContain("cmi.comments", attempt.Model.Keys); // the late commit changed nothing
    }

    [Fact]
    public async Task Commit_over_size_unknown_key_wrong_format_or_rate_is_refused()
    {
        var w = await _k.SetUpAsync("PC2", commitIntervalSeconds: 60);
        var (package, _) = await _k.AttachedAsync(w);
        var launch = await _k.LaunchAsync(w.Student, package.Id);
        var big = new string('x', PackageTrackingLimits.MaxValueBytes + 1);

        var unknownKey = await _k.CommitAsync(w.Student, launch.AttemptId, 0, ("cmi.core.student_name", "x"));
        var bigValue = await _k.CommitAsync(w.Student, launch.AttemptId, 0, ("cmi.suspend_data", big));
        var wrongFormat = await _k.Host.SendAsync(w.Student, HttpMethod.Post, Routes.PackageAttemptCommit(launch.AttemptId),
            new PackageCommitRequest(0, PackageCommitFormats.Xapi, new Dictionary<string, string> { ["verb"] = "completed" }));
        var bigMessage = await _k.CommitAsync(w.Student, launch.AttemptId, 0,
            ("cmi.suspend_data", new string('x', 4000)), ("cmi.comments", new string('x', 4000)), ("cmi.interactions.0.student_response", new string('x', 4000)));
        var negative = await _k.CommitAsync(w.Student, launch.AttemptId, -1, ("cmi.suspend_data", "x"));

        Assert.All(new[] { unknownKey, bigValue, wrongFormat, bigMessage, negative }, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        Assert.Equal(ProblemCodes.package_commit_rejected, await CodeAsync(unknownKey));
        Assert.Equal(-1, (await AttemptAsync(launch.AttemptId)).LastSeq); // nothing was applied

        Assert.Equal(HttpStatusCode.NoContent, (await _k.CommitAsync(w.Student, launch.AttemptId, 0, ("cmi.suspend_data", "ok"))).StatusCode);
        var tooFast = await _k.CommitAsync(w.Student, launch.AttemptId, 1, ("cmi.suspend_data", "fast"));
        Assert.Equal(HttpStatusCode.TooManyRequests, tooFast.StatusCode);
        Assert.Equal("ok", (await AttemptAsync(launch.AttemptId)).Model["cmi.suspend_data"]);
        // A lower seq is ignored before the rate is looked at: a late request is never punished.
        Assert.Equal(HttpStatusCode.NoContent, (await _k.CommitAsync(w.Student, launch.AttemptId, 0, ("cmi.suspend_data", "dup"))).StatusCode);
    }

    [Fact]
    public async Task The_data_model_of_one_attempt_is_capped_at_64_kb()
    {
        var w = await _k.SetUpAsync("PC3");
        var (package, _) = await _k.AttachedAsync(w);
        var launch = await _k.LaunchAsync(w.Student, package.Id);
        var refused = HttpStatusCode.NoContent;

        for (var i = 0; i < 12 && refused == HttpStatusCode.NoContent; i++)
        {
            var response = await _k.CommitAsync(w.Student, launch.AttemptId, i,
                ($"cmi.interactions.{2 * i}.student_response", new string('a', 3900)), ($"cmi.interactions.{2 * i + 1}.student_response", new string('b', 3900)));
            refused = response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.BadRequest, refused);
        Assert.InRange((await AttemptAsync(launch.AttemptId)).DataJson.Length, 1, PackageTrackingLimits.MaxCmiBytes);
    }

    [Fact]
    public async Task Other_student_staff_and_anonymous_commit_is_404()
    {
        var w = await _k.SetUpAsync("PC4");
        var (package, _) = await _k.AttachedAsync(w);
        var launch = await _k.LaunchAsync(w.Student, package.Id);
        var (other, _) = await _k.Host.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);

        foreach (var who in new[] { other, w.Stranger, w.Teacher, w.Admin })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _k.CommitAsync(who, launch.AttemptId, 0, ("cmi.suspend_data", "x"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _k.FinishAsync(who, launch.AttemptId, 0)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _k.CommitAsync(w.Student, Guid.NewGuid(), 0, ("cmi.suspend_data", "x"))).StatusCode);
        Assert.Equal(-1, (await AttemptAsync(launch.AttemptId)).LastSeq);
    }

    [Fact]
    public async Task Commit_after_the_module_is_hidden_is_404()
    {
        var w = await _k.SetUpAsync("PC5");
        var (package, module) = await _k.AttachedAsync(w);
        var launch = await _k.LaunchAsync(w.Student, package.Id);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var m = await db.Set<LessonModule>().SingleAsync(x => x.Id == module.Id);
            m.Apply(m.TitleAr, m.TitleEn, ModuleVisibilities.Hidden, null, null, m.Target, m.GroupId, m.CompletionRule, m.CompletionMinScore);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _k.CommitAsync(w.Student, launch.AttemptId, 0, ("cmi.suspend_data", "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _k.FinishAsync(w.Student, launch.AttemptId, 0)).StatusCode);
        Assert.Empty(await _k.GradeEventsAsync(package.Id));
    }

    [Fact]
    public async Task Commit_and_finish_after_the_enrollment_is_dropped_are_404_and_record_nothing()
    {
        var w = await _k.SetUpAsync("PC11");
        var (package, module) = await _k.AttachedAsync(w);
        var launch = await _k.LaunchAsync(w.Student, package.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await _k.CommitAsync(w.Student, launch.AttemptId, 0, ("cmi.core.lesson_status", "incomplete"))).StatusCode);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var enrollment = await db.Set<Lms.Domain.Enrollment.CourseEnrollment>()
                .SingleAsync(e => e.ClassCourseId == w.ClassCourse.Id && e.StudentId == w.StudentProfileId);
            enrollment.Drop(DateOnly.FromDateTime(DateTime.UtcNow), "dropped");
            await db.SaveChangesAsync();
        }

        // Launch and my-state are 404 after the drop, and so are the routes of the attempt (#605).
        Assert.Equal(HttpStatusCode.NotFound, (await _k.Host.SendAsync(w.Student, HttpMethod.Post, Routes.PackageLaunch(package.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _k.Host.SendAsync(w.Student, HttpMethod.Get, Routes.PackageMyState(package.Id))).StatusCode);
        var commit = await _k.CommitAsync(w.Student, launch.AttemptId, 1, ("cmi.core.lesson_status", "completed"), ("cmi.core.score.raw", "95"));
        var finish = await _k.FinishAsync(w.Student, launch.AttemptId, 1);

        Assert.Equal(HttpStatusCode.NotFound, commit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, finish.StatusCode);
        var attempt = await AttemptAsync(launch.AttemptId);
        Assert.Equal(0, attempt.LastSeq);
        Assert.True(attempt.IsOpen);
        Assert.Empty(await _k.GradeEventsAsync(package.Id));
        await using var verify = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await verify.Set<ModuleCompletion>().AnyAsync(c => c.ModuleId == module.Id && c.State == ModuleCompletion.CompletedState));
    }

    [Fact]
    public async Task Commit_and_finish_with_the_feature_flag_off_are_403_and_record_nothing()
    {
        var w = await _k.SetUpAsync("PC12");
        var (package, _) = await _k.AttachedAsync(w);
        var launch = await _k.LaunchAsync(w.Student, package.Id);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Remove(await db.Set<Lms.Domain.Configuration.OrgUnitSetting>()
                .SingleAsync(s => s.OrganizationId == w.Tree.Org.Id && s.Key == FeatureFlags.ScormContent.Name));
            await db.SaveChangesAsync();
        }

        var commit = await _k.CommitAsync(w.Student, launch.AttemptId, 0, ("cmi.core.lesson_status", "completed"), ("cmi.core.score.raw", "90"));
        var finish = await _k.FinishAsync(w.Student, launch.AttemptId, 0);

        Assert.Equal(HttpStatusCode.Forbidden, commit.StatusCode);
        Assert.Equal(ProblemCodes.feature_disabled, await CodeAsync(commit));
        Assert.Equal(HttpStatusCode.Forbidden, finish.StatusCode);
        Assert.Equal(-1, (await AttemptAsync(launch.AttemptId)).LastSeq);
        Assert.Empty(await _k.GradeEventsAsync(package.Id));
    }

    [Fact]
    public async Task Finish_judges_what_the_server_holds_closes_the_attempt_and_records_completion_and_grade()
    {
        var w = await _k.SetUpAsync("PC6");
        var (package, module) = await _k.AttachedAsync(w);
        var launch = await _k.LaunchAsync(w.Student, package.Id);
        await _k.CommitAsync(w.Student, launch.AttemptId, 0, ("cmi.core.lesson_status", "incomplete"), ("cmi.core.score.raw", "40"), ("cmi.core.score.max", "100"));

        var notYet = await (await _k.FinishAsync(w.Student, launch.AttemptId, 0)).Content.ReadFromJsonAsync<PackageAttemptDto>();
        Assert.False(notYet!.Completed); // the content has not completed: the attempt stays open and resumable
        Assert.Equal(PackageAttemptStatuses.InProgress, notYet.Status);
        Assert.Empty(await _k.GradeEventsAsync(package.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await _k.CommitAsync(w.Student, launch.AttemptId, 1,
            ("cmi.core.lesson_status", "passed"), ("cmi.core.score.raw", "85"))).StatusCode);

        var done = await (await _k.FinishAsync(w.Student, launch.AttemptId, 1)).Content.ReadFromJsonAsync<PackageAttemptDto>();

        Assert.True(done!.Completed);
        Assert.Equal(PackageAttemptStatuses.Completed, done.Status);
        Assert.Equal(85m, done.ScoreRaw);
        Assert.Equal(1, done.LastSeq);
        Assert.NotNull(done.CompletedAtUtc);
        var grade = Assert.Single(await _k.GradeEventsAsync(package.Id));
        Assert.Equal(85m, grade.Points);
        Assert.Equal(100m, grade.MaxPoints);
        Assert.Equal(Lms.Domain.Grading.GradeSourceKind.Scorm, grade.SourceKind);
        Assert.Equal(w.StudentProfileId, grade.StudentProfileId);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(ModuleCompletion.CompletedState, (await db.Set<ModuleCompletion>().SingleAsync(c => c.ModuleId == module.Id && c.StudentId == w.StudentProfileId)).State);
    }

    [Fact]
    public async Task Score_rule_completes_only_at_the_minimum_score()
    {
        var w = await _k.SetUpAsync("PC7");
        var (package, module) = await _k.AttachedAsync(w, rule: CompletionRules.Score, minScore: 70);

        var low = await _k.CompleteAsync(w.Student, package.Id, 50);
        Assert.False(low.Completed);
        var high = await (await _k.FinishAsync(w.Student, low.Id, 0)).Content.ReadFromJsonAsync<PackageAttemptDto>(); // same open attempt, finished again
        Assert.False(high!.Completed);
        Assert.Equal(HttpStatusCode.NoContent, (await _k.CommitAsync(w.Student, low.Id, 1, ("cmi.core.score.raw", "75"))).StatusCode);

        var passed = await (await _k.FinishAsync(w.Student, low.Id, 1)).Content.ReadFromJsonAsync<PackageAttemptDto>();

        Assert.True(passed!.Completed);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var completion = await db.Set<ModuleCompletion>().SingleAsync(c => c.ModuleId == module.Id && c.StudentId == w.StudentProfileId);
        Assert.Equal(ModuleCompletion.CompletedState, completion.State);
        Assert.Equal(75m, completion.Score);
    }

    [Fact]
    public async Task Commit_after_finish_is_409_and_a_second_finish_is_409()
    {
        var w = await _k.SetUpAsync("PC8");
        var (package, _) = await _k.AttachedAsync(w);
        var done = await _k.CompleteAsync(w.Student, package.Id, 90);

        var commit = await _k.CommitAsync(w.Student, done.Id, 5, ("cmi.suspend_data", "late"));
        var finish = await _k.FinishAsync(w.Student, done.Id, 0);

        Assert.Equal(HttpStatusCode.Conflict, commit.StatusCode);
        Assert.Equal(ProblemCodes.package_commit_rejected, await CodeAsync(commit));
        Assert.Equal(HttpStatusCode.Conflict, finish.StatusCode);
        Assert.Single(await _k.GradeEventsAsync(package.Id)); // nothing more was graded
    }

    [Fact]
    public async Task Finish_waits_for_the_commits_up_to_its_last_seq()
    {
        var w = await _k.SetUpAsync("PC9");
        var (package, _) = await _k.AttachedAsync(w);
        var launch = await _k.LaunchAsync(w.Student, package.Id);
        await _k.CommitAsync(w.Student, launch.AttemptId, 0, ("cmi.core.lesson_status", "incomplete"));

        // The page says it sent seq 1, which is still in flight: the finish waits for it and picks it up when it arrives.
        var finish = _k.FinishAsync(w.Student, launch.AttemptId, 1);
        await Task.Delay(500);
        await _k.CommitAsync(w.Student, launch.AttemptId, 1, ("cmi.core.lesson_status", "completed"), ("cmi.core.score.raw", "55"));

        var dto = await (await finish).Content.ReadFromJsonAsync<PackageAttemptDto>();
        Assert.True(dto!.Completed);
        Assert.Equal(1, dto.LastSeq);
    }

    [Fact]
    public async Task A_finish_for_a_commit_that_never_arrives_still_answers_with_what_the_server_has()
    {
        var w = await _k.SetUpAsync("PC10");
        var (package, _) = await _k.AttachedAsync(w);
        var launch = await _k.LaunchAsync(w.Student, package.Id);
        await _k.CommitAsync(w.Student, launch.AttemptId, 0, ("cmi.core.lesson_status", "completed"));

        var response = await _k.FinishAsync(w.Student, launch.AttemptId, 9);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PackageAttemptDto>();
        Assert.True(dto!.Completed);
        Assert.Equal(0, dto.LastSeq);
    }
}
