using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.Content;

/// <summary>
/// Review gaps of #391: a student never reaches the completion of a module they cannot see, and a lesson key (shared by every class
/// course of one blueprint) never reaches another class course.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ModuleIdorTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    [Fact]
    public async Task Completion_of_a_hidden_out_of_window_or_other_group_module_is_404_for_get_and_put()
    {
        var w = await _t.SetUpAsync("ID1");
        var now = DateTime.UtcNow;
        var other = await _t.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);
        var theirs = await _t.AddGroupAsync(w.ClassCourse, other.ProfileId);
        var visible = await _t.CreateAsync(w.Teacher, w, Page("visible", rule: CompletionRules.Manual));
        var hidden = await _t.CreateAsync(w.Teacher, w, Page("hidden", ModuleVisibilities.Hidden, CompletionRules.Manual));
        var future = await _t.CreateAsync(w.Teacher, w, Page("future", ModuleVisibilities.Scheduled, CompletionRules.Manual, from: now.AddHours(1)));
        var expired = await _t.CreateAsync(w.Teacher, w, Page("expired", ModuleVisibilities.Scheduled, CompletionRules.Manual, to: now.AddHours(-1)));
        var otherGroup = await _t.CreateAsync(w.Teacher, w, Page("group", rule: CompletionRules.Manual, target: ModuleTargets.Group, groupId: theirs.Id));

        foreach (var invisible in new[] { hidden, future, expired, otherGroup })
        {
            var get = await _t.SendAsync(w.Student, HttpMethod.Get, Routes.ModuleCompletion(invisible.Node.Id));
            var put = await _t.SendAsync(w.Student, HttpMethod.Put, Routes.ModuleCompletion(invisible.Node.Id));
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Student, HttpMethod.Put, Routes.ModuleCompletion(visible.Node.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Stranger, HttpMethod.Put, Routes.ModuleCompletion(visible.Node.Id))).StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var ids = new[] { hidden, future, expired, otherGroup }.Select(m => m.Node.Id).ToList();
        Assert.False(await db.Set<ModuleCompletion>().AnyAsync(c => ids.Contains(c.ModuleId))); // nothing was written for any of them
    }

    [Fact]
    public async Task A_lesson_key_shared_by_two_class_courses_never_reaches_the_other_one()
    {
        var w = await _t.SetUpAsync("ID2");
        var a = w.ClassCourse.Id;
        var b = w.OtherClassCourse.Id;
        var inB = await _t.CreateAsync(w.OtherTeacher, w with { ClassCourse = w.OtherClassCourse }, Page("only in B")); // the same lesson key, the other class course

        // The same Lesson1 key is in both plans; the teacher of A reaches only A.
        var list = await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.ClassCourseLessonModules(b, Lesson1));
        var create = await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ClassCourseLessonModules(b, Lesson1), Page());
        var reorder = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.ClassCourseLessonModulesOrder(b, Lesson1), new ReorderModulesRequest([inB.Node.Id]));
        var reverse = await _t.SendAsync(w.OtherTeacher, HttpMethod.Get, Routes.ClassCourseLessonModules(a, Lesson1));
        var get = await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.Module(inB.Node.Id));
        var update = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.Module(inB.Node.Id), new UpdateModuleRequest(
            "x", "x", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.None, null, new ModuleContentInput(BodyHtmlEn: "<p>x</p>")));
        var move = await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ModuleMove(inB.Node.Id), new MoveModuleRequest(Lesson2));
        var delete = await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.Module(inB.Node.Id));

        foreach (var response in new[] { list, create, reorder, reverse, get, update, move, delete })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // B is untouched: its module is still there, in its own lesson, and A got nothing.
        var still = (await (await _t.SendAsync(w.OtherTeacher, HttpMethod.Get, Routes.Module(inB.Node.Id))).Content.ReadFromJsonAsync<ModuleDetailDto>())!;
        Assert.Equal((b, Lesson1), (still.ClassCourseId, still.LessonKey));
        Assert.Empty((await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.ClassCourseLessonModules(a, Lesson1))).Content.ReadFromJsonAsync<List<ModuleNodeDto>>())!);
    }

    [Fact]
    public async Task Moving_a_module_by_a_shared_lesson_key_stays_inside_its_own_class_course()
    {
        var w = await _t.SetUpAsync("ID3");
        var mine = await _t.CreateAsync(w.Teacher, w, Page("mine"));

        var moved = (await (await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ModuleMove(mine.Node.Id), new MoveModuleRequest(Lesson2)))
            .Content.ReadFromJsonAsync<ModuleDetailDto>())!;

        Assert.Equal((w.ClassCourse.Id, Lesson2), (moved.ClassCourseId, moved.LessonKey));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var lesson = await db.Set<Lms.Domain.Classes.ClassLesson>().SingleAsync(l => l.Id == db.Set<LessonModule>().Single(m => m.Id == mine.Node.Id).ClassLessonId);
        var plan = await db.Set<Lms.Domain.Classes.ClassLessonPlan>().SingleAsync(p => p.Id == lesson.PlanId);
        Assert.Equal(w.ClassCourse.Id, plan.ClassCourseId); // the lesson row is A's, not the other plan's row with the same key
    }

    [Fact]
    public async Task A_module_made_for_an_assignment_or_quiz_cannot_be_deleted_through_the_module_api()
    {
        var w = await _t.SetUpAsync("ID4");
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<Lms.Api.Infrastructure.HttpCurrentContext>().SetUser(Guid.Empty, true);
            id = await scope.ServiceProvider.GetRequiredService<IModuleWriter>().CreateForRefAsync(
                w.ClassCourse.Id, Lesson1, ModuleKinds.Assignment, Guid.NewGuid(),
                new ModuleMetadata("واجب", "Homework", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.Submit, null));
        }

        var delete = await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.Module(id));
        var content = await _t.CreateAsync(w.Teacher, w, Page());
        var deleteContent = await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.Module(content.Node.Id));

        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        Assert.Equal(ProblemCodes.validation_failed, await CodeAsync(delete));
        Assert.Equal(HttpStatusCode.NoContent, deleteContent.StatusCode); // content kinds stay deletable
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.True(await db.Set<LessonModule>().AnyAsync(m => m.Id == id));
    }
}
