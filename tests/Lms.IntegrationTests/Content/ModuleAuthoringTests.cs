using System.Net;
using System.Net.Http.Json;
using Lms.Domain.AcademicYears;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.Content;

/// <summary>M4a Task 3 (FR-LP-05, FR-CNT-01, FR-CNT-02, D48): authoring modules under a lesson of a class course.</summary>
[Collection(ApiCollection.Name)]
public sealed class ModuleAuthoringTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    [Fact]
    public async Task Teacher_adds_page_and_link_modules_under_own_lesson()
    {
        var w = await _t.SetUpAsync("MA1");

        var page = await _t.CreateAsync(w.Teacher, w, Page(body: "<p>Hello <script>alert(1)</script><strong>world</strong></p>"));
        var link = await _t.CreateAsync(w.Teacher, w, Link());

        Assert.Equal([1, 2], [page.Node.Order, link.Node.Order]); // appended last
        Assert.Equal("<p>Hello <strong>world</strong></p>", page.Content.BodyHtmlEn); // sanitised on write
        Assert.Equal("https://example.com/a", link.Content.Url);
        Assert.True(page.CanEdit);
        var reread = (await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.Module(page.Node.Id))).Content.ReadFromJsonAsync<ModuleDetailDto>())!;
        Assert.Equal(link.Node.Id, reread.NextModuleId);
        Assert.Equal(page.Node.Id, link.PreviousModuleId);
        var list = await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.ClassCourseLessonModules(w.ClassCourse.Id, Lesson1));
        Assert.Equal([page.Node.Id, link.Node.Id], (await list.Content.ReadFromJsonAsync<List<ModuleNodeDto>>())!.Select(m => m.Id).ToArray());
    }

    [Fact]
    public async Task Teacher_of_another_class_course_and_students_cannot_author()
    {
        var w = await _t.SetUpAsync("MA2");
        var module = await _t.CreateAsync(w.Admin, w, Page());

        Assert.Equal(HttpStatusCode.NotFound, (await _t.CreateRawAsync(w.OtherTeacher, w, Page())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.CreateRawAsync(w.Student, w, Page())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Get, Routes.Module(module.Node.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Delete, Routes.Module(module.Node.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(w.Student, HttpMethod.Delete, Routes.Module(module.Node.Id))).StatusCode);
    }

    [Fact]
    public async Task Scheduled_requires_a_range_with_from_before_to_and_others_need_none()
    {
        var w = await _t.SetUpAsync("MA3");
        var now = DateTime.UtcNow;

        var ok = await _t.CreateRawAsync(w.Teacher, w, Page(visibility: ModuleVisibilities.Scheduled, from: now, to: now.AddDays(1)));
        var openEnded = await _t.CreateRawAsync(w.Teacher, w, Page(visibility: ModuleVisibilities.Scheduled, from: now));
        var neither = await _t.CreateRawAsync(w.Teacher, w, Page(visibility: ModuleVisibilities.Scheduled));
        var backwards = await _t.CreateRawAsync(w.Teacher, w, Page(visibility: ModuleVisibilities.Scheduled, from: now, to: now.AddDays(-1)));
        var publishedWithRange = await _t.CreateRawAsync(w.Teacher, w, Page(from: now));

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(HttpStatusCode.Created, openEnded.StatusCode);
        foreach (var bad in new[] { neither, backwards, publishedWithRange })
        {
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Equal(ProblemCodes.module_visibility_range_invalid, await CodeAsync(bad));
        }
    }

    [Fact]
    public async Task Group_target_must_be_a_group_of_this_class_course()
    {
        var w = await _t.SetUpAsync("MA4");
        var group = await _t.AddGroupAsync(w.ClassCourse, w.StudentProfileId);

        var ok = await _t.CreateRawAsync(w.Teacher, w, Page(target: ModuleTargets.Group, groupId: group.Id));
        var unknown = await _t.CreateRawAsync(w.Teacher, w, Page(target: ModuleTargets.Group, groupId: Guid.NewGuid()));
        var missing = await _t.CreateRawAsync(w.Teacher, w, Page(target: ModuleTargets.Group));
        var surplus = await _t.CreateRawAsync(w.Teacher, w, Page(groupId: group.Id));

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var created = (await ok.Content.ReadFromJsonAsync<ModuleDetailDto>())!;
        Assert.Equal(group.NameEn, created.Node.GroupNameEn);
        foreach (var bad in new[] { unknown, missing, surplus })
        {
            Assert.Equal(ProblemCodes.module_target_invalid, await CodeAsync(bad));
        }
    }

    [Fact]
    public async Task Invalid_bodies_are_400()
    {
        var w = await _t.SetUpAsync("MA5");

        var kinds = await _t.CreateRawAsync(w.Teacher, w, Page() with { Kind = ModuleKinds.Quiz }); // owned by its own feature
        var assetKind = await _t.CreateRawAsync(w.Teacher, w, Page() with { Kind = ModuleKinds.Video }); // arrives with assets
        var noTitle = await _t.CreateRawAsync(w.Teacher, w, Page() with { TitleEn = " " });
        var scoreRule = await _t.CreateRawAsync(w.Teacher, w, Page(rule: CompletionRules.Score));
        var minScore = await _t.CreateRawAsync(w.Teacher, w, Page() with { CompletionMinScore = 50 });
        var empty = await _t.CreateRawAsync(w.Teacher, w, Page(body: " "));
        var tooLong = await _t.CreateRawAsync(w.Teacher, w, Page(body: new string('a', 200_001)));
        var javascript = await _t.CreateRawAsync(w.Teacher, w, Link("javascript:alert(1)"));
        var relative = await _t.CreateRawAsync(w.Teacher, w, Link("/etc/passwd"));
        var tooLongUrl = await _t.CreateRawAsync(w.Teacher, w, Link("https://example.com/" + new string('a', 2_000)));

        foreach (var bad in new[] { kinds, assetKind, noTitle, scoreRule, minScore, empty, tooLong, javascript, relative, tooLongUrl })
        {
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Equal(ProblemCodes.validation_failed, await CodeAsync(bad));
        }
    }

    [Fact]
    public async Task Update_keeps_the_kind_and_re_sanitises()
    {
        var w = await _t.SetUpAsync("MA6");
        var page = await _t.CreateAsync(w.Teacher, w, Page());

        var response = await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.Module(page.Node.Id), new UpdateModuleRequest(
            "جديد", "Renamed", ModuleVisibilities.Hidden, null, null, ModuleTargets.ClassCourse, null, CompletionRules.Manual, null,
            new ModuleContentInput(BodyHtmlEn: "<p onclick=\"x()\">New</p><img src=x>")));

        var updated = (await response.Content.ReadFromJsonAsync<ModuleDetailDto>())!;
        Assert.Equal(("Renamed", ModuleVisibilities.Hidden, CompletionRules.Manual, ModuleKinds.Page),
            (updated.Node.TitleEn, updated.Node.Visibility, updated.Node.CompletionRule, updated.Node.Kind));
        Assert.Equal("<p>New</p>", updated.Content.BodyHtmlEn);
        Assert.False(updated.Node.IsVisibleNow);
    }

    [Fact]
    public async Task Reorder_requires_the_exact_live_set()
    {
        var w = await _t.SetUpAsync("MA7");
        var a = await _t.CreateAsync(w.Teacher, w, Page("A"));
        var b = await _t.CreateAsync(w.Teacher, w, Page("B"));
        var c = await _t.CreateAsync(w.Teacher, w, Page("C"));
        var url = Routes.ClassCourseLessonModulesOrder(w.ClassCourse.Id, Lesson1);

        var missing = await _t.SendAsync(w.Teacher, HttpMethod.Put, url, new ReorderModulesRequest([a.Node.Id, b.Node.Id]));
        var foreign = await _t.SendAsync(w.Teacher, HttpMethod.Put, url, new ReorderModulesRequest([a.Node.Id, b.Node.Id, Guid.NewGuid()]));
        var duplicated = await _t.SendAsync(w.Teacher, HttpMethod.Put, url, new ReorderModulesRequest([a.Node.Id, a.Node.Id, b.Node.Id]));
        var ok = await _t.SendAsync(w.Teacher, HttpMethod.Put, url, new ReorderModulesRequest([c.Node.Id, a.Node.Id, b.Node.Id]));

        foreach (var bad in new[] { missing, foreign, duplicated })
        {
            Assert.Equal(ProblemCodes.validation_failed, await CodeAsync(bad));
        }

        Assert.Equal([c.Node.Id, a.Node.Id, b.Node.Id], (await ok.Content.ReadFromJsonAsync<List<ModuleNodeDto>>())!.Select(m => m.Id).ToArray());
    }

    [Fact]
    public async Task Move_appends_to_another_lesson_of_the_same_plan_and_a_foreign_lesson_key_is_404()
    {
        var w = await _t.SetUpAsync("MA8");
        var first = await _t.CreateAsync(w.Teacher, w, Page("one"));
        var onLesson2 = await _t.CreateAsync(w.Teacher, w, Page("two"), Lesson2);

        var moved = await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ModuleMove(first.Node.Id), new MoveModuleRequest(Lesson2));
        var foreign = await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ModuleMove(first.Node.Id), new MoveModuleRequest(Guid.NewGuid()));

        var detail = (await moved.Content.ReadFromJsonAsync<ModuleDetailDto>())!;
        Assert.Equal((Lesson2, onLesson2.Node.Order + 1), (detail.LessonKey, detail.Node.Order));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_the_module_and_it_stays_out_of_the_lists()
    {
        var w = await _t.SetUpAsync("MA9");
        var module = await _t.CreateAsync(w.Teacher, w, Page());

        var delete = await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.Module(module.Node.Id));
        var get = await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.Module(module.Node.Id));
        var list = await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.ClassCourseLessonModules(w.ClassCourse.Id, Lesson1));

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Empty((await list.Content.ReadFromJsonAsync<List<ModuleNodeDto>>())!);
    }

    [Fact]
    public async Task Closed_class_rejects_module_writes_but_keeps_reads()
    {
        var w = await _t.SetUpAsync("MA10");
        var module = await _t.CreateAsync(w.Teacher, w, Page());
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Class>().SingleAsync(c => c.Id == w.Class.Id)).SetStatus(Class.ClosedStatus);
            await db.SaveChangesAsync();
        }

        var create = await _t.CreateRawAsync(w.Teacher, w, Page());
        var delete = await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.Module(module.Node.Id));
        var get = await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.Module(module.Node.Id));

        Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
        Assert.Equal(ProblemCodes.class_closed, await CodeAsync(create));
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact]
    public async Task Blueprint_push_keeps_teacher_modules_and_a_removed_lesson_hides_them_from_students()
    {
        var w = await _t.SetUpAsync("MA11");
        var module = await _t.CreateAsync(w.Teacher, w, Page(), Lesson2);

        // v2 of the blueprint drops lesson 2: it stays in the plan as hidden history, and so does the module.
        await Classes.LessonPlanTestKit.RunPushAsync(factory, await Classes.LessonPlanTestKit.PublishAsync(
            factory, w.Course, Lms.Shared.Blueprints.BlueprintApplyModes.PushToExisting,
            new Classes.TestSection(Section, "Week 1", new Classes.TestLesson(Lesson1, "Intro", 0))));

        var asTeacher = await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.Module(module.Node.Id));
        var asStudent = await _t.SendAsync(w.Student, HttpMethod.Get, Routes.Module(module.Node.Id));

        Assert.Equal(HttpStatusCode.OK, asTeacher.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, asStudent.StatusCode);
    }
}
