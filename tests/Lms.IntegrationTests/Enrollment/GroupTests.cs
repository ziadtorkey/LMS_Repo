using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Courses;
using Lms.Shared.Enrollment;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Enrollment;

/// <summary>M3 Task 27 (#235, FR-GRP-01..03, D6): groups of a class course with the one-group rule.</summary>
[Collection(ApiCollection.Name)]
public sealed class GroupTests(LmsApiFactory factory) : IDisposable
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private sealed record Setup(EnrollmentWorld World, RoleAssignment Admin, RoleAssignment Teacher, Guid Muslim, Guid Christian, Guid Other);

    /// <summary>A class with courses A and B, a Muslim, a Christian and a Muslim student placed in it, a teacher teaching course A.</summary>
    private async Task<Setup> SetUpAsync(string name)
    {
        var w = await EnrollmentWorld.CreateAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var teacher = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher, (await PeopleTestKit.AddUserAsync(factory)).Id);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var ccA = await db.Set<ClassCourse>().SingleAsync(c => c.Id == w.CcA.Id);
            db.Add(new ClassCourseTeacher(ccA, teacher.UserId, ClassCourseTeacher.PrimaryRole, Today));
            await db.SaveChangesAsync();
        }

        var muslim = await StudentAsync(w, "Muslim");
        var christian = await StudentAsync(w, "Christian");
        var other = await StudentAsync(w, "Muslim");
        using var scope = new EngineScope(factory.Services);
        var cls = await scope.Db.Set<Class>().SingleAsync(c => c.Id == w.Class.Id);
        var students = await scope.Db.Set<StudentProfile>().Where(s => new[] { muslim, christian, other }.Contains(s.Id)).ToListAsync();
        await scope.Engine.PlaceManyAsync(students, cls, Today, false, default);
        return new Setup(w, admin, teacher, muslim, christian, other);
    }

    private async Task<Guid> StudentAsync(EnrollmentWorld w, string religion)
    {
        var student = await PeopleTestKit.AddStudentAsync(factory, w.Tree.School1, PeopleTestKit.NewCode());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var tracked = await db.Set<StudentProfile>().SingleAsync(s => s.Id == student.Id);
        tracked.Update(tracked.NameAr, tracked.NameEn, null, null, tracked.DateOfBirth, tracked.Gender, religion, tracked.Nationality, null, null, null, null);
        await db.SaveChangesAsync();
        return student.Id;
    }

    private Task<HttpResponseMessage> SendAsync(RoleAssignment caller, HttpMethod method, string url, object? body = null) =>
        _host.SendAsync(method, url, caller, body);

    private static async Task<string?> CodeAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private async Task<StudentGroupDto> CreateAsync(RoleAssignment caller, Guid classCourseId, string name, string? type = null, string? value = null)
    {
        var response = await SendAsync(caller, HttpMethod.Post, Routes.ClassCourseGroups(classCourseId), new SaveGroupRequest("مجموعة " + name, name, type, value));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StudentGroupDto>())!;
    }

    private Task<HttpResponseMessage> AddAsync(RoleAssignment caller, Guid groupId, params Guid[] students) =>
        SendAsync(caller, HttpMethod.Post, Routes.GroupMembers(groupId), new GroupMembersRequest(students));

    [Fact]
    public async Task Teacher_creates_Islamic_and_Christian_groups_on_own_religion_class_course()
    {
        var s = await SetUpAsync("GR1");

        var islamic = await CreateAsync(s.Teacher, s.World.CcA.Id, "Islamic", CourseRestrictionTypes.Religion, "Muslim");
        var christian = await CreateAsync(s.Teacher, s.World.CcA.Id, "Christian", CourseRestrictionTypes.Religion, "Christian");

        Assert.Equal(("Religion", "Muslim", 0), (islamic.MatchType, islamic.MatchValue, islamic.MemberCount));
        Assert.Equal(HttpStatusCode.OK, (await AddAsync(s.Teacher, islamic.Id, s.Muslim, s.Other)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AddAsync(s.Teacher, christian.Id, s.Christian)).StatusCode);
        var list = (await (await SendAsync(s.Teacher, HttpMethod.Get, Routes.ClassCourseGroups(s.World.CcA.Id))).Content.ReadFromJsonAsync<List<StudentGroupDto>>())!;
        Assert.Equal([("Christian", 1), ("Islamic", 2)], list.Select(g => (g.NameEn, g.MemberCount)).ToArray());
        var members = (await (await SendAsync(s.Teacher, HttpMethod.Get, Routes.GroupMembers(islamic.Id))).Content.ReadFromJsonAsync<List<GroupMemberDto>>())!;
        Assert.Equal(new[] { s.Muslim, s.Other }.Order(), members.Select(m => m.StudentId).Order());
    }

    [Fact]
    public async Task Teacher_of_other_class_course_gets_404()
    {
        var s = await SetUpAsync("GR2");
        var group = await CreateAsync(s.Admin, s.World.CcB.Id, "On course B"); // the teacher teaches course A only

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(s.Teacher, HttpMethod.Post, Routes.ClassCourseGroups(s.World.CcB.Id), new SaveGroupRequest("م", "G", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(s.Teacher, HttpMethod.Get, Routes.ClassCourseGroups(s.World.CcB.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(s.Teacher, HttpMethod.Put, Routes.Group(group.Id), new SaveGroupRequest("م", "G2", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(s.Teacher, HttpMethod.Delete, Routes.Group(group.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AddAsync(s.Teacher, group.Id, s.Muslim)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(s.Teacher, HttpMethod.Get, Routes.GroupMembers(group.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(s.Teacher, HttpMethod.Get, Routes.Groups + $"/{group.Id}/suggestions")).StatusCode);
    }

    [Fact]
    public async Task Student_not_enrolled_is_400()
    {
        var s = await SetUpAsync("GR3");
        var group = await CreateAsync(s.Admin, s.World.CcOptional.Id, "Optional"); // nobody is enrolled in the optional course

        var response = await AddAsync(s.Admin, group.Id, s.Muslim);

        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.not_enrolled), (response.StatusCode, await CodeAsync(response)));
    }

    [Fact]
    public async Task Student_in_another_group_of_same_class_course_is_409_even_concurrently()
    {
        var s = await SetUpAsync("GR4");
        var first = await CreateAsync(s.Admin, s.World.CcA.Id, "First");
        var second = await CreateAsync(s.Admin, s.World.CcA.Id, "Second");
        await AddAsync(s.Admin, first.Id, s.Muslim);

        var taken = await AddAsync(s.Admin, second.Id, s.Muslim, s.Christian);
        Assert.Equal((HttpStatusCode.Conflict, ProblemCodes.already_in_group), (taken.StatusCode, await CodeAsync(taken)));
        // All or nothing: the Christian student was not added either.
        var members = (await (await SendAsync(s.Admin, HttpMethod.Get, Routes.GroupMembers(second.Id))).Content.ReadFromJsonAsync<List<GroupMemberDto>>())!;
        Assert.Empty(members);
        Assert.Equal(HttpStatusCode.OK, (await AddAsync(s.Admin, first.Id, s.Muslim)).StatusCode); // adding again to the same group changes nothing

        // Two requests at once for a student in neither group: exactly one wins (database unique key).
        var results = await Task.WhenAll(AddAsync(s.Admin, first.Id, s.Other), AddAsync(s.Admin, second.Id, s.Other));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await db.Set<GroupMember>().CountAsync(m => m.ClassCourseId == s.World.CcA.Id && m.StudentId == s.Other));
    }

    [Fact]
    public async Task Same_student_may_be_in_groups_of_two_class_courses()
    {
        var s = await SetUpAsync("GR5");
        var onA = await CreateAsync(s.Admin, s.World.CcA.Id, "On A");
        var onB = await CreateAsync(s.Admin, s.World.CcB.Id, "On B");

        Assert.Equal(HttpStatusCode.OK, (await AddAsync(s.Admin, onA.Id, s.Muslim)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AddAsync(s.Admin, onB.Id, s.Muslim)).StatusCode);
    }

    [Fact]
    public async Task Suggestions_list_ungrouped_muslim_students()
    {
        var s = await SetUpAsync("GR6");
        var islamic = await CreateAsync(s.Admin, s.World.CcA.Id, "Islamic", CourseRestrictionTypes.Religion, "Muslim");
        var free = await CreateAsync(s.Admin, s.World.CcA.Id, "Free");
        await AddAsync(s.Admin, free.Id, s.Other); // already in another group of the class course

        var suggestions = (await (await SendAsync(s.Admin, HttpMethod.Get, Routes.Groups + $"/{islamic.Id}/suggestions")).Content.ReadFromJsonAsync<List<GroupMemberDto>>())!;

        Assert.Equal([s.Muslim], suggestions.Select(m => m.StudentId).ToArray()); // not the Christian, not the grouped Muslim
        var all = (await (await SendAsync(s.Admin, HttpMethod.Get, Routes.Groups + $"/{free.Id}/suggestions")).Content.ReadFromJsonAsync<List<GroupMemberDto>>())!;
        Assert.Equal(new[] { s.Muslim, s.Christian }.Order(), all.Select(m => m.StudentId).Order()); // no rule: every ungrouped student
    }

    [Fact]
    public async Task Dropped_enrollment_removes_membership()
    {
        var s = await SetUpAsync("GR7");
        var group = await CreateAsync(s.Admin, s.World.CcA.Id, "Group");
        await AddAsync(s.Admin, group.Id, s.Muslim);
        Guid enrollmentId;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            enrollmentId = (await db.Set<CourseEnrollment>().SingleAsync(e => e.StudentId == s.Muslim && e.ClassCourseId == s.World.CcA.Id)).Id;
        }

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(s.Admin, HttpMethod.Post, Routes.EnrollmentDrop(enrollmentId), new DropEnrollmentRequest("left", null))).StatusCode);

        var members = (await (await SendAsync(s.Admin, HttpMethod.Get, Routes.GroupMembers(group.Id))).Content.ReadFromJsonAsync<List<GroupMemberDto>>())!;
        Assert.Empty(members);
        Assert.Equal(HttpStatusCode.BadRequest, (await AddAsync(s.Admin, group.Id, s.Muslim)).StatusCode); // no longer enrolled
    }

    [Fact]
    public async Task Counselor_can_view_not_manage_403()
    {
        var s = await SetUpAsync("GR8");
        var group = await CreateAsync(s.Admin, s.World.CcA.Id, "Group");
        var counselor = await AccessTestData.AssignAsync(factory, s.World.Tree, s.World.Tree.School1, BuiltInRoles.Counselor, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(counselor, HttpMethod.Get, Routes.ClassCourseGroups(s.World.CcA.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(counselor, HttpMethod.Get, Routes.GroupMembers(group.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(counselor, HttpMethod.Post, Routes.ClassCourseGroups(s.World.CcA.Id), new SaveGroupRequest("م", "G", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AddAsync(counselor, group.Id, s.Muslim)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(counselor, HttpMethod.Delete, Routes.Group(group.Id))).StatusCode);
    }

    [Fact]
    public async Task Group_rows_are_isolated_audited_and_validated()
    {
        var s = await SetUpAsync("GR9");
        var other = await SetUpAsync("GR9B");
        var group = await CreateAsync(s.Admin, s.World.CcA.Id, "Group", CourseRestrictionTypes.Gender, "Female");
        await AddAsync(s.Admin, group.Id, s.Muslim);

        // Another client and a sibling school see nothing.
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(other.Admin, HttpMethod.Get, Routes.GroupMembers(group.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(other.Admin, HttpMethod.Get, Routes.ClassCourseGroups(s.World.CcA.Id))).StatusCode);
        var school2Admin = await AccessTestData.AssignAsync(factory, s.World.Tree, s.World.Tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(school2Admin, HttpMethod.Get, Routes.GroupMembers(group.Id))).StatusCode);

        // Audited: group and member rows.
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.True(await db.AuditEntries.AnyAsync(e => e.EntityType == nameof(StudentGroup) && e.EntityId == group.Id && e.Action == AuditAction.Created));
        Assert.True(await db.AuditEntries.AnyAsync(e => e.EntityType == nameof(GroupMember) && e.Action == AuditAction.Created));

        // Validation: names, half a match rule, unknown value.
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(s.Admin, HttpMethod.Post, Routes.ClassCourseGroups(s.World.CcA.Id), new SaveGroupRequest(" ", "G", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(s.Admin, HttpMethod.Post, Routes.ClassCourseGroups(s.World.CcA.Id), new SaveGroupRequest("م", "G", "Religion", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(s.Admin, HttpMethod.Post, Routes.ClassCourseGroups(s.World.CcA.Id), new SaveGroupRequest("م", "G", "Religion", "Martian"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(s.Admin, HttpMethod.Post, Routes.ClassCourseGroups(s.World.CcA.Id), new SaveGroupRequest("م", "G", "Hobby", "Chess"))).StatusCode);

        // Edit and delete (the members go with the group and the slot is free again).
        var edited = await SendAsync(s.Admin, HttpMethod.Put, Routes.Group(group.Id), new SaveGroupRequest("مجموعة", "Renamed", null, null));
        Assert.Equal("Renamed", (await edited.Content.ReadFromJsonAsync<StudentGroupDto>())!.NameEn);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(s.Admin, HttpMethod.Delete, Routes.Group(group.Id))).StatusCode);
        var fresh = await CreateAsync(s.Admin, s.World.CcA.Id, "Fresh");
        Assert.Equal(HttpStatusCode.OK, (await AddAsync(s.Admin, fresh.Id, s.Muslim)).StatusCode);
    }

    [Fact]
    public async Task Closed_year_and_closed_class_block_every_group_write_with_409()
    {
        var s = await SetUpAsync("GR11");
        var group = await CreateAsync(s.Admin, s.World.CcA.Id, "Group");
        await AddAsync(s.Admin, group.Id, s.Muslim);

        async Task AssertAllWritesRefusedAsync(string code)
        {
            var responses = new[]
            {
                await SendAsync(s.Admin, HttpMethod.Post, Routes.ClassCourseGroups(s.World.CcA.Id), new SaveGroupRequest("م", "New", null, null)),
                await SendAsync(s.Admin, HttpMethod.Put, Routes.Group(group.Id), new SaveGroupRequest("م", "Renamed", null, null)),
                await SendAsync(s.Admin, HttpMethod.Delete, Routes.Group(group.Id)),
                await AddAsync(s.Admin, group.Id, s.Christian),
                await SendAsync(s.Admin, HttpMethod.Post, Routes.GroupMembersRemove(group.Id), new GroupMembersRequest([s.Muslim])),
            };
            foreach (var response in responses)
            {
                Assert.Equal((HttpStatusCode.Conflict, code), (response.StatusCode, await CodeAsync(response)));
            }

            // Reads stay open, and nothing changed.
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(s.Admin, HttpMethod.Get, Routes.ClassCourseGroups(s.World.CcA.Id))).StatusCode);
            var members = (await (await SendAsync(s.Admin, HttpMethod.Get, Routes.GroupMembers(group.Id))).Content.ReadFromJsonAsync<List<GroupMemberDto>>())!;
            Assert.Equal([s.Muslim], members.Select(m => m.StudentId).ToArray());
        }

        // Closed class (the year is open).
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Class>().SingleAsync(c => c.Id == s.World.Class.Id)).SetStatus(Class.ClosedStatus);
            await db.SaveChangesAsync();
        }

        await AssertAllWritesRefusedAsync(ProblemCodes.class_closed);

        // Open class, closed year.
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Class>().SingleAsync(c => c.Id == s.World.Class.Id)).SetStatus(Class.ActiveStatus);
            (await db.Set<Lms.Domain.AcademicYears.AcademicYear>().SingleAsync(y => y.Id == s.World.Year.Id)).Close();
            await db.SaveChangesAsync();
        }

        await AssertAllWritesRefusedAsync(ProblemCodes.year_closed);
    }

    [Fact]
    public async Task Group_targets_accept_only_live_groups_of_that_class_course()
    {
        var s = await SetUpAsync("GR10");
        var group = await CreateAsync(s.Admin, s.World.CcA.Id, "Target");
        using var scope = new EngineScope(factory.Services);
        var targets = scope.Services.GetRequiredService<IGroupTargets>();

        Assert.True(await targets.IsValidTargetAsync(s.World.CcA.Id, group.Id, default));
        Assert.False(await targets.IsValidTargetAsync(s.World.CcB.Id, group.Id, default)); // another class course
        Assert.False(await targets.IsValidTargetAsync(s.World.CcA.Id, Guid.NewGuid(), default));
        await SendAsync(s.Admin, HttpMethod.Delete, Routes.Group(group.Id));
        Assert.False(await targets.IsValidTargetAsync(s.World.CcA.Id, group.Id, default)); // deleted
    }
}
