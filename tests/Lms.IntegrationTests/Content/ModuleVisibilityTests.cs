using System.Net;
using System.Net.Http.Json;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Classes;
using Lms.Shared.Content;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.Content;

/// <summary>M4a Task 3 (FR-CNT-02, D51): the one visibility resolver. A student never receives a module they may not see.</summary>
[Collection(ApiCollection.Name)]
public sealed class ModuleVisibilityTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private async Task<List<ModuleNodeDto>> ListAsync(Lms.Domain.Access.RoleAssignment caller, ContentWorld w)
    {
        var response = await _t.SendAsync(caller, HttpMethod.Get, Routes.ClassCourseLessonModules(w.ClassCourse.Id, Lesson1));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<ModuleNodeDto>>())!;
    }

    [Fact]
    public async Task Student_tree_hides_hidden_out_of_window_and_other_group_modules()
    {
        var w = await _t.SetUpAsync("MV1");
        var now = DateTime.UtcNow;
        var other = await _t.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);
        var mine = await _t.AddGroupAsync(w.ClassCourse, w.StudentProfileId);
        var theirs = await _t.AddGroupAsync(w.ClassCourse, other.ProfileId);

        var published = await _t.CreateAsync(w.Teacher, w, Page("published"));
        var hidden = await _t.CreateAsync(w.Teacher, w, Page("hidden", ModuleVisibilities.Hidden));
        var open = await _t.CreateAsync(w.Teacher, w, Page("open", ModuleVisibilities.Scheduled, from: now.AddHours(-1), to: now.AddHours(1)));
        var future = await _t.CreateAsync(w.Teacher, w, Page("future", ModuleVisibilities.Scheduled, from: now.AddHours(1)));
        var expired = await _t.CreateAsync(w.Teacher, w, Page("expired", ModuleVisibilities.Scheduled, to: now.AddHours(-1)));
        var forMine = await _t.CreateAsync(w.Teacher, w, Page("mine", target: ModuleTargets.Group, groupId: mine.Id));
        var forTheirs = await _t.CreateAsync(w.Teacher, w, Page("theirs", target: ModuleTargets.Group, groupId: theirs.Id));

        var asStudent = await ListAsync(w.Student, w);
        var asOther = await ListAsync(other.Student, w);
        var asTeacher = await ListAsync(w.Teacher, w);

        Assert.Equal([published.Node.Id, open.Node.Id, forMine.Node.Id], asStudent.Select(m => m.Id).ToArray());
        Assert.Equal([published.Node.Id, open.Node.Id, forTheirs.Node.Id], asOther.Select(m => m.Id).ToArray());
        Assert.Equal(7, asTeacher.Count); // staff get every module, with the visibility chip
        Assert.Equal([true, false, true, false, false, true, true], asTeacher.Select(m => m.IsVisibleNow).ToArray());
        Assert.All(asStudent, m => Assert.Equal(CompletionStates.NotStarted, m.CompletionState));
        Assert.All(asTeacher, m => Assert.Null(m.CompletionState));
        Assert.Contains(hidden.Node.Id, asTeacher.Select(m => m.Id));
        Assert.Contains(future.Node.Id, asTeacher.Select(m => m.Id));
        Assert.Contains(expired.Node.Id, asTeacher.Select(m => m.Id));
    }

    [Fact]
    public async Task Student_get_of_an_invisible_module_is_404_and_never_the_payload()
    {
        var w = await _t.SetUpAsync("MV2");
        var group = await _t.AddGroupAsync(w.ClassCourse, (await _t.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse)).ProfileId);
        var hidden = await _t.CreateAsync(w.Teacher, w, Page("secret", ModuleVisibilities.Hidden, body: "<p>secret</p>"));
        var future = await _t.CreateAsync(w.Teacher, w, Page("later", ModuleVisibilities.Scheduled, from: DateTime.UtcNow.AddDays(1)));
        var otherGroup = await _t.CreateAsync(w.Teacher, w, Page("group", target: ModuleTargets.Group, groupId: group.Id));
        var visible = await _t.CreateAsync(w.Teacher, w, Page("ok"));

        foreach (var invisible in new[] { hidden, future, otherGroup })
        {
            var response = await _t.SendAsync(w.Student, HttpMethod.Get, Routes.Module(invisible.Node.Id));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
        }

        var ok = await _t.SendAsync(w.Student, HttpMethod.Get, Routes.Module(visible.Node.Id));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Stranger, HttpMethod.Get, Routes.Module(visible.Node.Id))).StatusCode);
    }

    [Fact]
    public async Task Detail_neighbours_follow_what_the_caller_can_see()
    {
        var w = await _t.SetUpAsync("MV3");
        var a = await _t.CreateAsync(w.Teacher, w, Page("a"));
        var hidden = await _t.CreateAsync(w.Teacher, w, Page("hidden", ModuleVisibilities.Hidden));
        var c = await _t.CreateAsync(w.Teacher, w, Page("c"));
        var onLesson2 = await _t.CreateAsync(w.Teacher, w, Page("d"), Lesson2);

        var student = (await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.Module(a.Node.Id))).Content.ReadFromJsonAsync<ModuleDetailDto>())!;
        var teacher = (await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.Module(a.Node.Id))).Content.ReadFromJsonAsync<ModuleDetailDto>())!;
        var last = (await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.Module(c.Node.Id))).Content.ReadFromJsonAsync<ModuleDetailDto>())!;

        Assert.Equal(c.Node.Id, student.NextModuleId); // the hidden one is skipped for the student
        Assert.Equal(hidden.Node.Id, teacher.NextModuleId);
        Assert.Equal(onLesson2.Node.Id, last.NextModuleId); // across lessons, in plan order
        Assert.False(student.CanEdit);
        Assert.True(teacher.CanEdit);
        Assert.True(student.ServerNowUtc > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Plan_tree_nests_modules_for_staff_and_filters_them_for_students()
    {
        var w = await _t.SetUpAsync("MV4");
        var visible = await _t.CreateAsync(w.Teacher, w, Page("visible", rule: CompletionRules.View));
        await _t.CreateAsync(w.Teacher, w, Page("hidden", ModuleVisibilities.Hidden));

        var staff = (await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.ClassCourseLessonPlan(w.ClassCourse.Id)))
            .Content.ReadFromJsonAsync<ClassLessonPlanDto>())!;
        var student = (await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.ClassCourseLessonPlan(w.ClassCourse.Id)))
            .Content.ReadFromJsonAsync<ClassLessonPlanDto>())!;

        Assert.Equal(2, staff.Sections.Single().Lessons.First().Modules!.Count);
        Assert.Null(staff.Progress);
        Assert.Equal([visible.Node.Id], student.Sections.Single().Lessons.First().Modules!.Select(m => m.Id).ToArray());
        Assert.Equal(new ProgressDto(0, 1), student.Progress);
        Assert.Equal(new ProgressDto(0, 1), student.Sections.Single().Lessons.First().Progress);
        Assert.Equal(new ProgressDto(0, 0), student.Sections.Single().Lessons.Last().Progress);
        Assert.NotNull(student.ServerNowUtc);
    }
}
