using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Classes;

/// <summary>Records lifecycle calls; can veto removal or fail an add to prove the transaction.</summary>
public sealed class RecordingLifecycleHandler(Lms.Infrastructure.Persistence.LmsDbContext db) : IClassCourseLifecycleHandler
{
    public static readonly ConcurrentQueue<string> Calls = new();
    public static volatile bool VetoRemoval;
    public static volatile bool FailAdd;

    public async Task OnAddedAsync(ClassCourse classCourse, CancellationToken cancellationToken)
    {
        // The row must already be written inside the same transaction (a handler writes rows that point at it).
        Calls.Enqueue($"added:{classCourse.Id}:{await db.Set<ClassCourse>().AnyAsync(c => c.Id == classCourse.Id, cancellationToken)}");
        if (FailAdd)
        {
            throw new InvalidOperationException("handler failed");
        }
    }

    public Task OnRemovingAsync(ClassCourse classCourse, CancellationToken cancellationToken)
    {
        Calls.Enqueue($"removing:{classCourse.Id}");
        return VetoRemoval ? throw new ClassCourseInUseException("has data") : Task.CompletedTask;
    }

    public Task OnOptionalChangedAsync(ClassCourse classCourse, CancellationToken cancellationToken)
    {
        Calls.Enqueue($"optional:{classCourse.Id}:{classCourse.IsOptional}");
        return Task.CompletedTask;
    }
}

[Collection(ApiCollection.Name)]
public sealed class ClassCourseTests : IDisposable
{
    private readonly LmsApiFactory _factory;
    private readonly ClassTestHost _h;

    public ClassCourseTests(LmsApiFactory factory)
    {
        _factory = factory;
        _h = new ClassTestHost(factory, s => s.AddScoped<IClassCourseLifecycleHandler, RecordingLifecycleHandler>());
        RecordingLifecycleHandler.VetoRemoval = false;
        RecordingLifecycleHandler.FailAdd = false;
    }

    public void Dispose() => _h.Dispose();

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<(ClassTestHost.World W, ClassSummaryDto Class, Course Course)> SetUpAsync(string name)
    {
        var w = await _h.SeedAsync(name);
        var course = await AcademicTestKit.AddCourseAsync(_factory, w.Tree.Root);
        await AcademicTestKit.AllocateAsync(_factory, course, w.Tree.Root);
        var cls = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year));
        return (w, cls, course);
    }

    private Task<HttpResponseMessage> AddAsync(ClassTestHost.World w, Guid classId, bool optional, params Guid[] courseIds) =>
        _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassCoursesOfClass(classId), new AddClassCoursesRequest(courseIds, optional));

    private async Task<List<ClassCourseDto>> ListAsync(Lms.Domain.Access.RoleAssignment caller, Guid classId)
    {
        var response = await _h.SendAsync(caller, HttpMethod.Get, Routes.ClassCoursesOfClass(classId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<ClassCourseDto>>())!;
    }

    [Fact]
    public async Task Course_allocated_above_can_be_added_to_class()
    {
        var (w, cls, course) = await SetUpAsync("CC1");

        var response = await AddAsync(w, cls.Id, false, course.Id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var added = Assert.Single((await response.Content.ReadFromJsonAsync<List<ClassCourseDto>>())!);
        Assert.Equal(course.Id, added.CourseId);
        Assert.Equal(course.Code, added.CourseCode);
        Assert.False(added.IsOptional);
        Assert.Equal(cls.Id, added.ClassId);
        Assert.Equal(0, added.EnrolledCount);
        Assert.Empty(added.CurrentTeachers);
        Assert.Equal(added.Id, Assert.Single(await ListAsync(w.Admin, cls.Id)).Id);
    }

    [Fact]
    public async Task Course_not_allocated_for_class_unit_or_inactive_is_400_and_adds_nothing()
    {
        var (w, cls, good) = await SetUpAsync("CC2");
        var unallocated = await AcademicTestKit.AddCourseAsync(_factory, w.Tree.School2); // owned at School 2, never allocated at School 1
        var other = await _h.SeedAsync("CC2B");
        var foreign = await AcademicTestKit.AddCourseAsync(_factory, other.Tree.Root);
        await AcademicTestKit.AllocateAsync(_factory, foreign, other.Tree.Root);
        var inactive = await AcademicTestKit.AddCourseAsync(_factory, w.Tree.Root);
        await AcademicTestKit.AllocateAsync(_factory, inactive, w.Tree.Root);
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Course>().SingleAsync(c => c.Id == inactive.Id)).SetStatus(Course.InactiveStatus);
            await db.SaveChangesAsync();
        }

        foreach (var bad in new[] { unallocated.Id, foreign.Id, inactive.Id, Guid.NewGuid() })
        {
            var response = await AddAsync(w, cls.Id, false, good.Id, bad);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ProblemCodes.course_not_available, await ClassTestHost.CodeAsync(response));
        }

        Assert.Empty(await ListAsync(w.Admin, cls.Id));
        Assert.Equal(HttpStatusCode.BadRequest, (await AddAsync(w, cls.Id, false)).StatusCode);
    }

    [Fact]
    public async Task Existing_course_is_409_and_nothing_else_is_added()
    {
        var (w, cls, course) = await SetUpAsync("CC3");
        var second = await AcademicTestKit.AddCourseAsync(_factory, w.Tree.Root);
        await AcademicTestKit.AllocateAsync(_factory, second, w.Tree.Root);
        await AddAsync(w, cls.Id, false, course.Id);

        var response = await AddAsync(w, cls.Id, true, second.Id, course.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.class_course_exists, await ClassTestHost.CodeAsync(response));
        Assert.Single(await ListAsync(w.Admin, cls.Id));
    }

    [Fact]
    public async Task Adding_course_calls_lifecycle_handlers_in_transaction()
    {
        var (w, cls, course) = await SetUpAsync("CC4");

        var ok = await AddAsync(w, cls.Id, false, course.Id);
        var id = (await ok.Content.ReadFromJsonAsync<List<ClassCourseDto>>())![0].Id;

        Assert.Contains($"added:{id}:True", RecordingLifecycleHandler.Calls);

        // A handler failure rolls the whole add back.
        var second = await AcademicTestKit.AddCourseAsync(_factory, w.Tree.Root);
        await AcademicTestKit.AllocateAsync(_factory, second, w.Tree.Root);
        RecordingLifecycleHandler.FailAdd = true;
        try
        {
            var failed = await AddAsync(w, cls.Id, false, second.Id);
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        }
        finally
        {
            RecordingLifecycleHandler.FailAdd = false;
        }

        Assert.Single(await ListAsync(w.Admin, cls.Id));
    }

    [Fact]
    public async Task Optional_flag_changes_call_handler_and_handler_veto_blocks_removal_409()
    {
        var (w, cls, course) = await SetUpAsync("CC5");
        var id = (await (await AddAsync(w, cls.Id, false, course.Id)).Content.ReadFromJsonAsync<List<ClassCourseDto>>())![0].Id;

        var update = await _h.SendAsync(w.Admin, HttpMethod.Put, Routes.ClassCourse(id), new UpdateClassCourseRequest(true));
        RecordingLifecycleHandler.VetoRemoval = true;
        var vetoed = await _h.SendAsync(w.Admin, HttpMethod.Delete, Routes.ClassCourse(id));
        RecordingLifecycleHandler.VetoRemoval = false;
        var removed = await _h.SendAsync(w.Admin, HttpMethod.Delete, Routes.ClassCourse(id));

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.True((await update.Content.ReadFromJsonAsync<ClassCourseDto>())!.IsOptional);
        Assert.Contains($"optional:{id}:True", RecordingLifecycleHandler.Calls);
        Assert.Equal(HttpStatusCode.Conflict, vetoed.StatusCode);
        Assert.Equal(ProblemCodes.class_course_in_use, await ClassTestHost.CodeAsync(vetoed));
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty(await ListAsync(w.Admin, cls.Id));
    }

    [Fact]
    public async Task Bulk_assign_adds_to_all_classes_of_grade_and_skips_existing()
    {
        var (w, first, course) = await SetUpAsync("CC6");
        var second = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year));
        var third = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School2, w.Year));
        var closed = await _h.CreateAsync(w.Admin, ClassTestHost.Save(w.Tree.School1, w.Year));
        await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassClose(closed.Id));
        await AddAsync(w, first.Id, false, course.Id);

        var response = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassCoursesBulkAssign,
            new BulkAssignCourseRequest(w.Tree.Root.Id, w.Year.Id, course.Id, true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(2, body.GetProperty("added").GetInt32());   // second and third
        Assert.Equal(2, body.GetProperty("skipped").GetInt32()); // first has it, the closed class is read-only
        Assert.True(Assert.Single(await ListAsync(w.Admin, second.Id)).IsOptional);
        Assert.Single(await ListAsync(w.Admin, third.Id));
        Assert.Empty(await ListAsync(w.Admin, closed.Id));
        Assert.False(Assert.Single(await ListAsync(w.Admin, first.Id)).IsOptional); // existing one untouched
    }

    [Fact]
    public async Task Closed_class_or_year_blocks_changes_409()
    {
        var (w, cls, course) = await SetUpAsync("CC7");
        var id = (await (await AddAsync(w, cls.Id, false, course.Id)).Content.ReadFromJsonAsync<List<ClassCourseDto>>())![0].Id;
        await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassClose(cls.Id));
        var second = await AcademicTestKit.AddCourseAsync(_factory, w.Tree.Root);
        await AcademicTestKit.AllocateAsync(_factory, second, w.Tree.Root);

        var add = await AddAsync(w, cls.Id, false, second.Id);
        var update = await _h.SendAsync(w.Admin, HttpMethod.Put, Routes.ClassCourse(id), new UpdateClassCourseRequest(true));
        var delete = await _h.SendAsync(w.Admin, HttpMethod.Delete, Routes.ClassCourse(id));

        foreach (var response in new[] { add, update, delete })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(ProblemCodes.class_closed, await ClassTestHost.CodeAsync(response));
        }
    }

    [Fact]
    public async Task Teacher_lists_only_own_class_courses_and_cannot_manage()
    {
        var (w, cls, course) = await SetUpAsync("CC8");
        var other = await AcademicTestKit.AddCourseAsync(_factory, w.Tree.Root);
        await AcademicTestKit.AllocateAsync(_factory, other, w.Tree.Root);
        var added = (await (await AddAsync(w, cls.Id, false, course.Id, other.Id)).Content.ReadFromJsonAsync<List<ClassCourseDto>>())!;
        var user = await PeopleTestKit.AddUserAsync(_factory);
        var teacher = await AccessTestData.AssignAsync(_factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher, user.Id);
        var mine = added.Single(a => a.CourseId == course.Id);
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var cc = await db.Set<ClassCourse>().SingleAsync(c => c.Id == mine.Id);
            db.Add(new ClassCourseTeacher(cc, user.Id, ClassCourseTeacher.PrimaryRole, Today.AddDays(-1)));
            await db.SaveChangesAsync();
        }

        var list = await ListAsync(teacher, cls.Id);

        var row = Assert.Single(list);
        Assert.Equal(mine.Id, row.Id);
        Assert.Equal(user.Id, Assert.Single(row.CurrentTeachers).UserId);
        Assert.Equal(HttpStatusCode.Forbidden, (await _h.SendAsync(teacher, HttpMethod.Post, Routes.ClassCoursesOfClass(cls.Id),
            new AddClassCoursesRequest([other.Id], false))).StatusCode);
    }

    [Fact]
    public async Task Other_clients_admin_gets_404_everywhere()
    {
        var (w, cls, course) = await SetUpAsync("CC9");
        var id = (await (await AddAsync(w, cls.Id, false, course.Id)).Content.ReadFromJsonAsync<List<ClassCourseDto>>())![0].Id;
        var other = await _h.SeedAsync("CC9B");

        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(other.Admin, HttpMethod.Get, Routes.ClassCoursesOfClass(cls.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(other.Admin, HttpMethod.Post, Routes.ClassCoursesOfClass(cls.Id),
            new AddClassCoursesRequest([course.Id], false))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(other.Admin, HttpMethod.Put, Routes.ClassCourse(id), new UpdateClassCourseRequest(true))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _h.SendAsync(other.Admin, HttpMethod.Delete, Routes.ClassCourse(id))).StatusCode);
    }

    [Fact]
    public async Task Copy_with_courses_copies_class_courses()
    {
        var (w, cls, course) = await SetUpAsync("CC10");
        var target = await _h.AddYearAsync(w.Tree.Root);
        var second = await AcademicTestKit.AddCourseAsync(_factory, w.Tree.Root);
        await AcademicTestKit.AllocateAsync(_factory, second, w.Tree.Root);
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(w, cls.Id, false, course.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(w, cls.Id, true, second.Id)).StatusCode);

        var response = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassesCopy,
            new CopyClassesRequest(w.Year.Id, target.Id, w.Tree.School1.Id, true, null, null));

        Assert.Equal(1, (await response.Content.ReadFromJsonAsync<CopyClassesResultDto>())!.Created);
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var copy = await db.Set<Class>().SingleAsync(c => c.AcademicYearId == target.Id);
        var rows = await db.Set<ClassCourse>().Where(c => c.ClassId == copy.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows.Count(r => r.IsOptional));
        Assert.Equal(2, RecordingLifecycleHandler.Calls.Count(c => rows.Any(r => c.StartsWith($"added:{r.Id}:True"))));
    }

    private async Task<(ClassCourseDto Cc, Guid EnrollmentId, Guid StudentId)> EnrolAsync(
        ClassTestHost.World w, ClassSummaryDto cls, ClassCourseDto classCourse, string source, bool inGroup = false)
    {
        var student = await PeopleTestKit.AddStudentAsync(_factory, w.Tree.School1, PeopleTestKit.NewCode());
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var tracked = await db.Set<ClassCourse>().SingleAsync(c => c.Id == classCourse.Id);
        var placement = new ClassStudent(await db.Set<Class>().SingleAsync(c => c.Id == cls.Id), student.Id, Today.AddDays(-1));
        db.Add(placement);
        await db.SaveChangesAsync();
        var enrollment = new Lms.Domain.Enrollment.CourseEnrollment(tracked, placement, source, Today.AddDays(-1));
        db.Add(enrollment);
        await db.SaveChangesAsync();
        if (inGroup)
        {
            var group = new Lms.Domain.Enrollment.StudentGroup(tracked, "مجموعة", "Group", null, null);
            db.Add(group);
            await db.SaveChangesAsync();
            db.Add(new Lms.Domain.Enrollment.GroupMember(group, enrollment));
            await db.SaveChangesAsync();
        }

        return (classCourse, enrollment.Id, student.Id);
    }

    [Fact]
    public async Task Removal_is_blocked_by_group_members_and_non_auto_enrollments_and_drops_auto_ones_D41()
    {
        var (w, cls, course) = await SetUpAsync("CC11");
        var courses = new List<Course>();
        for (var i = 0; i < 3; i++)
        {
            var extra = await AcademicTestKit.AddCourseAsync(_factory, w.Tree.Root);
            await AcademicTestKit.AllocateAsync(_factory, extra, w.Tree.Root);
            courses.Add(extra);
        }

        var added = (await (await AddAsync(w, cls.Id, false, courses[0].Id, courses[1].Id, courses[2].Id)).Content.ReadFromJsonAsync<List<ClassCourseDto>>())!;
        var withGroup = await EnrolAsync(w, cls, added[0], "Auto", inGroup: true);
        var withManual = await EnrolAsync(w, cls, added[1], "Manual");
        var autoOnly = await EnrolAsync(w, cls, added[2], "Auto");

        var group = await _h.SendAsync(w.Admin, HttpMethod.Delete, Routes.ClassCourse(withGroup.Cc.Id));
        var manual = await _h.SendAsync(w.Admin, HttpMethod.Delete, Routes.ClassCourse(withManual.Cc.Id));
        var auto = await _h.SendAsync(w.Admin, HttpMethod.Delete, Routes.ClassCourse(autoOnly.Cc.Id));

        Assert.Equal(HttpStatusCode.Conflict, group.StatusCode);
        Assert.Equal(ProblemCodes.class_course_in_use, await ClassTestHost.CodeAsync(group));
        Assert.Equal(HttpStatusCode.Conflict, manual.StatusCode);
        Assert.Equal(ProblemCodes.class_course_in_use, await ClassTestHost.CodeAsync(manual));
        Assert.Equal(HttpStatusCode.NoContent, auto.StatusCode);
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var dropped = await db.Set<Lms.Domain.Enrollment.CourseEnrollment>().SingleAsync(e => e.Id == autoOnly.EnrollmentId);
        Assert.Equal(Lms.Domain.Enrollment.CourseEnrollment.DroppedStatus, dropped.Status);
        Assert.NotNull(dropped.To);
        // The blocked class courses and their enrollments are untouched.
        Assert.Equal(Lms.Domain.Enrollment.CourseEnrollment.ActiveStatus,
            (await db.Set<Lms.Domain.Enrollment.CourseEnrollment>().SingleAsync(e => e.Id == withManual.EnrollmentId)).Status);
        Assert.Equal(2, await db.Set<ClassCourse>().CountAsync(c => c.ClassId == cls.Id));
    }

    [Fact]
    public async Task Adding_more_than_100_courses_or_bulk_assigning_to_over_200_classes_is_400_too_many_items()
    {
        var (w, cls, course) = await SetUpAsync("CC12");

        var tooManyCourses = await AddAsync(w, cls.Id, false, Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray());
        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.AddRange(Enumerable.Range(0, 200).Select(i => new Class(w.Tree.School2, w.Year.Id, "BG" + i, "ا", "N", 5)));
            await db.SaveChangesAsync();
        }

        var tooManyClasses = await _h.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassCoursesBulkAssign,
            new BulkAssignCourseRequest(w.Tree.Root.Id, w.Year.Id, course.Id, false)); // 200 + the setup class = 201

        Assert.Equal(HttpStatusCode.BadRequest, tooManyCourses.StatusCode);
        Assert.Equal(ProblemCodes.too_many_items, await ClassTestHost.CodeAsync(tooManyCourses));
        Assert.Equal(HttpStatusCode.BadRequest, tooManyClasses.StatusCode);
        Assert.Equal(ProblemCodes.too_many_items, await ClassTestHost.CodeAsync(tooManyClasses));
    }

    [Fact]
    public async Task Parallel_adds_of_the_same_course_end_in_one_201_and_409s_never_500()
    {
        var (w, cls, course) = await SetUpAsync("CC13");

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => AddAsync(w, cls.Id, false, course.Id)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            Assert.Equal(ProblemCodes.class_course_exists, await ClassTestHost.CodeAsync(conflict));
        }

        Assert.Single(await ListAsync(w.Admin, cls.Id));
    }
}
