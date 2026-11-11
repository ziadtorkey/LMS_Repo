using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Organizations;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Classes;

[Collection(ApiCollection.Name)]
public sealed class ClassEndpointsTests(LmsApiFactory factory) : IDisposable
{
    private readonly ClassTestHost _h = new(factory);

    public void Dispose() => _h.Dispose();

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Admin_creates_class_in_school_for_current_year()
    {
        var w = await _h.SeedAsync("CL1");

        var dto = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year, "g1-a"));

        Assert.Equal("G1-A", dto.Code);
        Assert.Equal(w.Tree.School1.Id, dto.OrgUnitId);
        Assert.Equal(w.Year.Id, dto.AcademicYearId);
        Assert.Equal("Active", dto.Status);
        Assert.False(dto.IsReadOnly);
        Assert.Equal(0, dto.StudentCount);
        Assert.Equal(HttpStatusCode.OK, (await _h.SendAsync(w.Admin, HttpMethod.Get, Routes.Class(dto.Id))).StatusCode);
    }

    [Fact]
    public async Task Unit_without_CanHostClasses_is_400()
    {
        var w = await _h.SeedAsync("CL2");
        OrgUnit grouping;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var level = new LevelType(w.Tree.Org.Id, "تجميع", "Grouping", 2, false);
            grouping = OrgUnit.CreateChild(w.Tree.School1, level.Id, "قسم", "Dept", "D1");
            db.AddRange(level, grouping);
            await db.SaveChangesAsync();
        }

        var response = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.Classes, ClassTestHost.Save(grouping, w.Year));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.invalid_level_type, await ClassTestHost.CodeAsync(response));
    }

    [Fact]
    public async Task Year_not_effective_at_unit_is_400_and_closed_year_is_409()
    {
        var w = await _h.SeedAsync("CL3");
        var schoolTwoYear = await _h.AddYearAsync(w.Tree.School2); // defined at School 2: not effective at School 1
        var closed = await _h.AddYearAsync(w.Tree.Root, closed: true);

        var notEffective = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.Classes, ClassTestHost.Save(w.Tree.School1, schoolTwoYear));
        var closedYear = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.Classes, ClassTestHost.Save(w.Tree.School1, closed));

        Assert.Equal(HttpStatusCode.BadRequest, notEffective.StatusCode);
        Assert.Equal(ProblemCodes.year_not_effective, await ClassTestHost.CodeAsync(notEffective));
        Assert.Equal(HttpStatusCode.Conflict, closedYear.StatusCode);
        Assert.Equal(ProblemCodes.year_closed, await ClassTestHost.CodeAsync(closedYear));
    }

    [Fact]
    public async Task Code_unique_per_year_not_across_years()
    {
        var w = await _h.SeedAsync("CL4");
        var nextYear = await _h.AddYearAsync(w.Tree.Root);
        await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year, "A1"));

        var duplicate = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.Classes, ClassTestHost.Save(w.Tree.School2, w.Year, "a1"));
        var otherYear = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.Classes, ClassTestHost.Save(w.Tree.School1, nextYear, "A1"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(ProblemCodes.class_code_taken, await ClassTestHost.CodeAsync(duplicate));
        Assert.Equal(HttpStatusCode.Created, otherYear.StatusCode);
    }

    [Fact]
    public async Task Invalid_fields_are_400()
    {
        var w = await _h.SeedAsync("CL5");
        var good = ClassTestHost.Save(w.Tree.School1, w.Year);

        foreach (var bad in new[] { good with { Code = "bad code!" }, good with { NameEn = " " }, good with { Capacity = 0 }, good with { Capacity = 501 } })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.Classes, bad)).StatusCode);
        }
    }

    [Fact]
    public async Task Homeroom_teacher_needs_a_teacher_assignment_at_or_above_the_unit()
    {
        var w = await _h.SeedAsync("CL6");
        var outsider = await PeopleTestKit.AddUserAsync(factory);
        var teacherUser = await PeopleTestKit.AddUserAsync(factory);
        await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.Root, BuiltInRoles.Teacher, teacherUser.Id); // at an ancestor

        var refused = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.Classes, ClassTestHost.Save(w.Tree.School1, w.Year, homeroom: outsider.Id));
        var dto = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year, homeroom: teacherUser.Id));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(ProblemCodes.teacher_not_in_unit, await ClassTestHost.CodeAsync(refused));
        Assert.Equal(teacherUser.Id, dto.HomeroomTeacherUserId);
        Assert.Equal(teacherUser.DisplayNameEn, dto.HomeroomTeacherNameEn);
    }

    [Fact]
    public async Task Update_changes_fields_but_not_unit_or_year_and_capacity_cannot_drop_below_students()
    {
        var w = await _h.SeedAsync("CL7");
        var dto = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year, "U1", capacity: 20));
        await PlaceAsync(dto.Id, await PeopleTestKit.AddStudentAsync(factory, w.Tree.School1, PeopleTestKit.NewCode()));
        await PlaceAsync(dto.Id, await PeopleTestKit.AddStudentAsync(factory, w.Tree.School1, PeopleTestKit.NewCode()));

        var ok = await _h.SendAsync(w.Admin, HttpMethod.Put, Routes.Class(dto.Id),
            new SaveClassRequest("U1", "اسم جديد", "New name", w.Tree.School1.Id, w.Year.Id, 25, null));
        var moved = await _h.SendAsync(w.Admin, HttpMethod.Put, Routes.Class(dto.Id),
            new SaveClassRequest("U1", "ا", "N", w.Tree.School2.Id, w.Year.Id, 25, null));
        var tooSmall = await _h.SendAsync(w.Admin, HttpMethod.Put, Routes.Class(dto.Id),
            new SaveClassRequest("U1", "ا", "N", w.Tree.School1.Id, w.Year.Id, 1, null));
        var exact = await _h.SendAsync(w.Admin, HttpMethod.Put, Routes.Class(dto.Id),
            new SaveClassRequest("U1", "ا", "N", w.Tree.School1.Id, w.Year.Id, 2, null));

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("New name", (await ok.Content.ReadFromJsonAsync<ClassSummaryDto>())!.NameEn);
        Assert.Equal(HttpStatusCode.BadRequest, moved.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooSmall.StatusCode);
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
    }

    [Fact]
    public async Task Closed_class_rejects_update_and_delete_409()
    {
        var w = await _h.SeedAsync("CL8");
        var dto = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year));

        var close = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassClose(dto.Id));
        var update = await _h.SendAsync(w.Admin, HttpMethod.Put, Routes.Class(dto.Id),
            new SaveClassRequest(dto.Code, "ا", "N", w.Tree.School1.Id, w.Year.Id, 5, null));
        var delete = await _h.SendAsync(w.Admin, HttpMethod.Delete, Routes.Class(dto.Id));

        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        var closed = (await close.Content.ReadFromJsonAsync<ClassSummaryDto>())!;
        Assert.Equal("Closed", closed.Status);
        Assert.True(closed.IsReadOnly);
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Equal(ProblemCodes.class_closed, await ClassTestHost.CodeAsync(update));
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassClose(dto.Id))).StatusCode);
    }

    [Fact]
    public async Task Delete_class_with_students_or_courses_is_409_and_empty_class_is_deleted()
    {
        var w = await _h.SeedAsync("CL9");
        var withStudent = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year));
        var withCourse = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year));
        var empty = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year));
        await PlaceAsync(withStudent.Id, await PeopleTestKit.AddStudentAsync(factory, w.Tree.School1, PeopleTestKit.NewCode()));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var cls = await db.Set<Class>().SingleAsync(c => c.Id == withCourse.Id);
            var course = await AcademicTestKit.AddCourseAsync(factory, w.Tree.School1);
            db.Add(new ClassCourse(cls, course, false));
            await db.SaveChangesAsync();
        }

        var a = await _h.SendAsync(w.Admin, HttpMethod.Delete, Routes.Class(withStudent.Id));
        var b = await _h.SendAsync(w.Admin, HttpMethod.Delete, Routes.Class(withCourse.Id));
        var c = await _h.SendAsync(w.Admin, HttpMethod.Delete, Routes.Class(empty.Id));

        Assert.Equal(ProblemCodes.class_in_use, await ClassTestHost.CodeAsync(a));
        Assert.Equal(HttpStatusCode.Conflict, b.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, c.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(w.Admin, HttpMethod.Get, Routes.Class(empty.Id))).StatusCode);
        // The code can be reused after the delete (live key cleared).
        Assert.Equal(HttpStatusCode.Created,
            (await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.Classes, ClassTestHost.Save(w.Tree.School1, w.Year, empty.Code))).StatusCode);
    }

    [Fact]
    public async Task CreateClasses_policy_blocks_create()
    {
        var w = await _h.SeedAsync("CL10");
        await _h.Host.AddPolicyAsync(w.Tree, w.Tree.Root, PolicyCatalog.CreateClasses);

        var response = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.Classes, ClassTestHost.Save(w.Tree.School1, w.Year));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("BlockedByPolicy", (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Teacher_lists_only_own_classes()
    {
        var w = await _h.SeedAsync("CL11");
        var mine = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year));
        var other = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year));
        var user = await PeopleTestKit.AddUserAsync(factory);
        var teacher = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher, user.Id);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var cls = await db.Set<Class>().SingleAsync(c => c.Id == mine.Id);
            var course = await AcademicTestKit.AddCourseAsync(factory, w.Tree.School1);
            var cc = new ClassCourse(cls, course, false);
            db.AddRange(cc, new ClassCourseTeacher(cc, user.Id, ClassCourseTeacher.PrimaryRole, Today.AddDays(-1)));
            await db.SaveChangesAsync();
        }

        var list = await _h.SendAsync(teacher, HttpMethod.Get, Routes.Classes);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var page = (await list.Content.ReadFromJsonAsync<PagedResult<ClassSummaryDto>>())!;
        Assert.Equal(mine.Id, Assert.Single(page.Items).Id);
        Assert.Equal(HttpStatusCode.OK, (await _h.SendAsync(teacher, HttpMethod.Get, Routes.Class(mine.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(teacher, HttpMethod.Get, Routes.Class(other.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _h.SendAsync(teacher, HttpMethod.Post, Routes.Classes,
            ClassTestHost.Save(w.Tree.School1, w.Year))).StatusCode);
    }

    [Fact]
    public async Task List_defaults_to_current_year_and_filters_by_search_status_and_unit()
    {
        var w = await _h.SeedAsync("CL12");
        var nextYear = await _h.AddYearAsync(w.Tree.Root);
        var current = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year, "CUR"));
        var otherSchool = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School2, w.Year, "SCH2"));
        var next = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, nextYear, "NXT"));
        await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassClose(otherSchool.Id));

        async Task<List<Guid>> Ids(string query) =>
            (await (await _h.SendAsync(w.Admin, HttpMethod.Get, Routes.Classes + query)).Content.ReadFromJsonAsync<PagedResult<ClassSummaryDto>>())!
                .Items.Select(i => i.Id).ToList();

        var byDefault = await Ids($"?orgUnitId={w.Tree.Root.Id}");
        Assert.Contains(current.Id, byDefault);
        Assert.DoesNotContain(next.Id, byDefault);
        Assert.Equal([next.Id], await Ids($"?orgUnitId={w.Tree.Root.Id}&academicYearId={nextYear.Id}"));
        Assert.Equal([current.Id], await Ids($"?orgUnitId={w.Tree.Root.Id}&search=cur"));
        Assert.Equal([otherSchool.Id], await Ids($"?orgUnitId={w.Tree.Root.Id}&status=Closed"));
        Assert.Equal([current.Id], await Ids($"?orgUnitId={w.Tree.School1.Id}"));
        Assert.Equal(HttpStatusCode.BadRequest, (await _h.SendAsync(w.Admin, HttpMethod.Get, Routes.Classes + "?pageSize=201")).StatusCode);
    }

    [Fact]
    public async Task Class_changes_are_audited()
    {
        var w = await _h.SeedAsync("CL13");
        var dto = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year, "AUD"));
        await _h.SendAsync(w.Admin, HttpMethod.Put, Routes.Class(dto.Id),
            new SaveClassRequest("AUD", "ا", "Renamed", w.Tree.School1.Id, w.Year.Id, 5, null));
        await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassClose(dto.Id));

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entries = await db.AuditEntries.Where(e => e.EntityId == dto.Id).OrderBy(e => e.TimestampUtc).ThenBy(e => e.Id).ToListAsync();

        Assert.Equal(AuditAction.Created, entries[0].Action);
        Assert.All(entries, e => Assert.Equal(nameof(Class), e.EntityType));
        Assert.True(entries.Count >= 3);
        Assert.All(entries, e => Assert.Equal(w.Tree.School1.Path, e.OrgUnitPath));
    }

    [Fact]
    public async Task Other_clients_admin_cannot_see_or_change_the_class()
    {
        var w = await _h.SeedAsync("CL14");
        var other = await _h.SeedAsync("CL14B");
        var dto = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year));

        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(other.Admin, HttpMethod.Get, Routes.Class(dto.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(other.Admin, HttpMethod.Post, Routes.ClassClose(dto.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(other.Admin, HttpMethod.Delete, Routes.Class(dto.Id))).StatusCode);
        Assert.Empty((await (await _h.SendAsync(other.Admin, HttpMethod.Get, Routes.Classes)).Content.ReadFromJsonAsync<PagedResult<ClassSummaryDto>>())!.Items);
    }

    private async Task PlaceAsync(Guid classId, Lms.Domain.People.StudentProfile student)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var cls = await db.Set<Class>().SingleAsync(c => c.Id == classId);
        db.Add(new ClassStudent(cls, student.Id, Today.AddDays(-1)));
        await db.SaveChangesAsync();
    }
}
