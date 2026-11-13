using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.Content;

/// <summary>M4a Task 6 (FR-CNT-04): completion states, rollups and the continue module.</summary>
[Collection(ApiCollection.Name)]
public sealed class CompletionTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private async Task<ModuleCompletionDto> CompletionOfAsync(Lms.Domain.Access.RoleAssignment caller, Guid moduleId)
    {
        var response = await _t.SendAsync(caller, HttpMethod.Get, Routes.ModuleCompletion(moduleId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ModuleCompletionDto>())!;
    }

    private async Task<CourseProgressDto> ProgressAsync(Lms.Domain.Access.RoleAssignment caller, ContentWorld w, Guid? studentId = null)
    {
        var url = studentId is { } id ? Routes.ClassCourseProgressOf(w.ClassCourse.Id, id) : Routes.ClassCourseProgress(w.ClassCourse.Id);
        var response = await _t.SendAsync(caller, HttpMethod.Get, url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CourseProgressDto>())!;
    }

    [Fact]
    public async Task Manual_mark_is_idempotent()
    {
        var w = await _t.SetUpAsync("CP1");
        var module = await _t.CreateAsync(w.Teacher, w, Page(rule: CompletionRules.Manual));

        var first = await _t.SendAsync(w.Student, HttpMethod.Put, Routes.ModuleCompletion(module.Node.Id));
        var second = await _t.SendAsync(w.Student, HttpMethod.Put, Routes.ModuleCompletion(module.Node.Id));

        var a = (await first.Content.ReadFromJsonAsync<ModuleCompletionDto>())!;
        var b = (await second.Content.ReadFromJsonAsync<ModuleCompletionDto>())!;
        Assert.Equal(CompletionStates.Completed, a.State);
        Assert.Equal(a.CompletedAtUtc, b.CompletedAtUtc); // the first completion time stays
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await db.Set<ModuleCompletion>().CountAsync(c => c.ModuleId == module.Node.Id));
    }

    [Fact]
    public async Task Mark_on_a_non_manual_rule_is_409_and_staff_have_no_completion()
    {
        var w = await _t.SetUpAsync("CP2");
        var view = await _t.CreateAsync(w.Teacher, w, Page(rule: CompletionRules.View));
        var none = await _t.CreateAsync(w.Teacher, w, Page());

        foreach (var module in new[] { view, none })
        {
            var response = await _t.SendAsync(w.Student, HttpMethod.Put, Routes.ModuleCompletion(module.Node.Id));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(ProblemCodes.completion_not_manual, await CodeAsync(response));
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.ModuleCompletion(view.Node.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.ModuleCompletion(view.Node.Id))).StatusCode);
    }

    [Fact]
    public async Task Opening_a_view_rule_module_completes_it_and_any_other_rule_only_starts_it()
    {
        var w = await _t.SetUpAsync("CP3");
        var view = await _t.CreateAsync(w.Teacher, w, Page("view", rule: CompletionRules.View));
        var manual = await _t.CreateAsync(w.Teacher, w, Page("manual", rule: CompletionRules.Manual));

        Assert.Equal(CompletionStates.NotStarted, (await CompletionOfAsync(w.Student, view.Node.Id)).State);
        await _t.SendAsync(w.Student, HttpMethod.Get, Routes.Module(view.Node.Id));
        await _t.SendAsync(w.Student, HttpMethod.Get, Routes.Module(manual.Node.Id));
        await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.Module(view.Node.Id)); // a teacher viewing records nothing

        Assert.Equal(CompletionStates.Completed, (await CompletionOfAsync(w.Student, view.Node.Id)).State);
        Assert.Equal(CompletionStates.InProgress, (await CompletionOfAsync(w.Student, manual.Node.Id)).State);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(2, await db.Set<ModuleCompletion>().CountAsync(c => c.StudentId == w.StudentProfileId));
    }

    [Fact]
    public async Task Hidden_or_none_rule_modules_do_not_count_and_progress_rolls_up_per_lesson_and_course()
    {
        var w = await _t.SetUpAsync("CP4");
        var a = await _t.CreateAsync(w.Teacher, w, Page("a", rule: CompletionRules.Manual));
        var b = await _t.CreateAsync(w.Teacher, w, Page("b", rule: CompletionRules.View));
        await _t.CreateAsync(w.Teacher, w, Page("none")); // rule None
        await _t.CreateAsync(w.Teacher, w, Page("hidden", ModuleVisibilities.Hidden, CompletionRules.Manual)); // not visible
        var c = await _t.CreateAsync(w.Teacher, w, Page("c", rule: CompletionRules.Manual), Lesson2);
        await _t.SendAsync(w.Student, HttpMethod.Put, Routes.ModuleCompletion(a.Node.Id));
        await _t.SendAsync(w.Student, HttpMethod.Get, Routes.Module(b.Node.Id));

        var progress = await ProgressAsync(w.Student, w);

        Assert.Equal(new ProgressDto(2, 3), progress.Course);
        Assert.Equal([new ProgressDto(2, 2), new ProgressDto(0, 1)], progress.Lessons.Select(l => l.Progress).ToArray());
        Assert.Equal([Lesson1, Lesson2], progress.Lessons.Select(l => l.LessonKey).ToArray());
        Assert.Equal(3, progress.Modules.Count);
        Assert.Equal(c.Node.Id, progress.NextModuleId); // the first visible counted module that is not done, in plan order
        Assert.Equal(w.StudentProfileId, progress.StudentId);
    }

    [Fact]
    public async Task Teacher_reads_an_enrolled_students_progress_and_another_teacher_gets_404()
    {
        var w = await _t.SetUpAsync("CP5");
        var module = await _t.CreateAsync(w.Teacher, w, Page(rule: CompletionRules.Manual));
        await _t.SendAsync(w.Student, HttpMethod.Put, Routes.ModuleCompletion(module.Node.Id));
        var notEnrolled = await _t.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var enrollment = await db.Set<Lms.Domain.Enrollment.CourseEnrollment>()
                .SingleAsync(e => e.ClassCourseId == w.ClassCourse.Id && e.StudentId == notEnrolled.ProfileId);
            enrollment.Drop(DateOnly.FromDateTime(DateTime.UtcNow), "left");
            await db.SaveChangesAsync();
        }

        var asTeacher = await ProgressAsync(w.Teacher, w, w.StudentProfileId);
        var other = await _t.SendAsync(w.OtherTeacher, HttpMethod.Get, Routes.ClassCourseProgressOf(w.ClassCourse.Id, w.StudentProfileId));
        var missingId = await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.ClassCourseProgress(w.ClassCourse.Id));
        var dropped = await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.ClassCourseProgressOf(w.ClassCourse.Id, notEnrolled.ProfileId));
        var stranger = await _t.SendAsync(w.Stranger, HttpMethod.Get, Routes.ClassCourseProgress(w.ClassCourse.Id));
        var peek = await ProgressAsync(w.Student, w, notEnrolled.ProfileId); // a student's studentId is ignored: still their own

        Assert.Equal(new ProgressDto(1, 1), asTeacher.Course);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingId.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, dropped.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(w.StudentProfileId, peek.StudentId);
    }

    [Fact]
    public async Task Concurrent_views_record_one_completion()
    {
        var w = await _t.SetUpAsync("CP6");
        var module = await _t.CreateAsync(w.Teacher, w, Page(rule: CompletionRules.View));

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => _t.SendAsync(w.Student, HttpMethod.Get, Routes.Module(module.Node.Id))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await db.Set<ModuleCompletion>().CountAsync(c => c.ModuleId == module.Node.Id));
    }

    [Fact]
    public async Task Tree_with_a_large_plan_answers_quickly()
    {
        var w = await _t.SetUpAsync("CP7");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var cc = await db.Set<Lms.Domain.Classes.ClassCourse>().SingleAsync(c => c.Id == w.ClassCourse.Id);
            var plan = await db.Set<Lms.Domain.Classes.ClassLessonPlan>().SingleAsync(p => p.ClassCourseId == cc.Id);
            var lesson = await db.Set<Lms.Domain.Classes.ClassLesson>().SingleAsync(l => l.PlanId == plan.Id && l.Key == Lesson1);
            for (var i = 1; i <= 400; i++)
            {
                var module = new LessonModule(cc, lesson, ModuleKinds.Page, i);
                module.Apply("ص" + i, "P" + i, ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.Manual, null);
                db.Add(module);
            }

            await db.SaveChangesAsync();
        }

        await _t.SendAsync(w.Student, HttpMethod.Get, Routes.ClassCourseLessonPlan(w.ClassCourse.Id)); // warm up
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var response = await _t.SendAsync(w.Student, HttpMethod.Get, Routes.ClassCourseLessonPlan(w.ClassCourse.Id));
        watch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(watch.ElapsedMilliseconds < 300, $"{watch.ElapsedMilliseconds} ms");
    }
}
