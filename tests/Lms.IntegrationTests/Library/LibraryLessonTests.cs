using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Library;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.Library;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.Library;

/// <summary>Add a library resource to a lesson as a LibraryResource module (M4a Task 12, FR-LIB-01).</summary>
[Collection(ApiCollection.Name)]
public sealed class LibraryLessonTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private static ModuleSettingsInput Settings(
        string rule = CompletionRules.Manual, string visibility = ModuleVisibilities.Published, DateTime? from = null, DateTime? to = null) =>
        new("مورد", "Resource", visibility, from, to, ModuleTargets.ClassCourse, null, rule, null);

    private async Task<Guid> ResourceAsync(OrgUnit owner)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var resource = new LibraryResource(owner, "مورد", "Resource", null, null, LibraryResourceTypes.Link, null, null, null, "https://example.org", true);
        db.Add(resource);
        await db.SaveChangesAsync();
        return resource.Id;
    }

    private Task<HttpResponseMessage> AddAsync(ContentWorld w, Lms.Domain.Access.RoleAssignment who, Guid resourceId, ModuleSettingsInput? settings = null, Guid? classCourse = null) =>
        _t.SendAsync(who, HttpMethod.Post, Routes.LibraryResourceAddToLesson(resourceId),
            new AddLibraryResourceToLessonRequest(classCourse ?? w.ClassCourse.Id, Lesson1, settings ?? Settings()));

    [Fact]
    public async Task Teacher_adds_a_visible_resource_to_an_own_lesson_and_the_student_sees_it()
    {
        var w = await _t.SetUpAsync("LL1");
        var atSchool = await ResourceAsync(w.Tree.School1);
        var atRoot = await ResourceAsync(w.Tree.Root); // inherited from above is visible at the class course's unit

        var first = await AddAsync(w, w.Teacher, atSchool);
        var second = await AddAsync(w, w.Teacher, atRoot, Settings(CompletionRules.View));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var node = (await first.Content.ReadFromJsonAsync<ModuleNodeDto>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))!;
        Assert.Equal((ModuleKinds.LibraryResource, 1, CompletionRules.Manual), (node.Kind, node.Order, node.CompletionRule));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(atSchool, (await db.Set<Lms.Domain.Content.LessonModule>().SingleAsync(m => m.Id == node.Id)).RefId);
        var asStudent = await _t.SendAsync(w.Student, HttpMethod.Get, Routes.ClassCourseLessonModules(w.ClassCourse.Id, Lesson1));
        Assert.Equal(2, (await asStudent.Content.ReadFromJsonAsync<List<ModuleNodeDto>>())!.Count);
    }

    [Fact]
    public async Task Resource_of_a_sibling_school_or_a_stranger_class_course_is_404_and_students_cannot_add()
    {
        var w = await _t.SetUpAsync("LL2");
        var sibling = await ResourceAsync(w.Tree.School2);
        var mine = await ResourceAsync(w.Tree.School1);

        Assert.Equal(HttpStatusCode.NotFound, (await AddAsync(w, w.Admin, sibling)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AddAsync(w, w.OtherTeacher, mine)).StatusCode); // not their class course
        Assert.Equal(HttpStatusCode.NotFound, (await AddAsync(w, w.Teacher, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AddAsync(w, w.Student, mine)).StatusCode);
    }

    [Fact]
    public async Task Rule_range_and_lesson_are_validated()
    {
        var w = await _t.SetUpAsync("LL3");
        var resource = await ResourceAsync(w.Tree.School1);

        var submit = await AddAsync(w, w.Teacher, resource, Settings(CompletionRules.Submit));
        var range = await AddAsync(w, w.Teacher, resource, Settings(visibility: ModuleVisibilities.Scheduled));
        var lesson = await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.LibraryResourceAddToLesson(resource),
            new AddLibraryResourceToLessonRequest(w.ClassCourse.Id, Guid.NewGuid(), Settings()));

        Assert.Equal(HttpStatusCode.BadRequest, submit.StatusCode);
        Assert.Equal((HttpStatusCode.BadRequest, "module_visibility_range_invalid"), (range.StatusCode, await LibraryTestWorld.CodeAsync(range)));
        Assert.Equal(HttpStatusCode.BadRequest, lesson.StatusCode);
    }

    [Fact]
    public async Task Closed_class_refuses_and_deleting_the_resource_removes_its_modules()
    {
        var w = await _t.SetUpAsync("LL4");
        var resource = await ResourceAsync(w.Tree.School1);
        var added = (await (await AddAsync(w, w.Teacher, resource)).Content.ReadFromJsonAsync<ModuleNodeDto>())!;

        Assert.Equal(HttpStatusCode.NoContent, (await _t.SendAsync(w.Admin, HttpMethod.Delete, Routes.LibraryResource(resource))).StatusCode);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            Assert.False(await db.Set<Lms.Domain.Content.LessonModule>().AnyAsync(m => m.Id == added.Id));
            (await db.Set<Lms.Domain.Classes.Class>().SingleAsync(c => c.Id == w.Class.Id)).SetStatus(Lms.Domain.Classes.Class.ClosedStatus);
            await db.SaveChangesAsync();
        }

        var other = await ResourceAsync(w.Tree.School1);
        var closed = await AddAsync(w, w.Teacher, other);
        Assert.Equal((HttpStatusCode.Conflict, "class_closed"), (closed.StatusCode, await LibraryTestWorld.CodeAsync(closed)));
    }
}
