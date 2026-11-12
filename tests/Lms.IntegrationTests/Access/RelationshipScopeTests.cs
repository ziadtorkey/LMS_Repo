using System.Net;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.Infrastructure;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Access;

/// <summary>Relationship scopes (M3 Task 12; Review Focus 1): own class courses, supervised courses, own enrollments.</summary>
[Collection(ApiCollection.Name)]
public sealed class RelationshipScopeTests(LmsApiFactory factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record World(AccessTree Tree, Domain.AcademicYears.AcademicYear Year);

    private async Task<World> SeedAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        return new World(tree, await AcademicTestKit.AddYearAsync(factory, tree.School1));
    }

    private (TestCurrentContext Context, RelationshipScope Scope, LmsDbContext Db) ScopeFor(
        AccessTree tree, RoleAssignment assignment, bool platform = false)
    {
        var context = platform
            ? TestCurrentContext.PlatformAdmin()
            : new TestCurrentContext
            {
                UserId = assignment.UserId, OrganizationId = tree.Org.Id, AssignmentId = assignment.Id, ScopePath = assignment.OrgUnitPath,
            };
        var db = factory.CreateDbContext(context);
        return (context, new RelationshipScope(db, context, new SystemClock()), db);
    }

    private async Task<Guid> UserIdAsync() => (await PeopleTestKit.AddUserAsync(factory)).Id;

    private async Task<ClassCourse> CourseInAsync(Class cls)
    {
        var course = await AcademicTestKit.AddCourseAsync(factory, await UnitAsync(cls));
        return await AcademicTestKit.AddClassCourseAsync(factory, cls, course);
    }

    private async Task<Lms.Domain.Organizations.OrgUnit> UnitAsync(Class cls)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.OrgUnits.SingleAsync(u => u.Id == cls.OrgUnitId);
    }

    private async Task<ClassCourseTeacher> TeachAsync(ClassCourse cc, Guid userId, DateOnly from, DateOnly? to = null)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var teacher = new ClassCourseTeacher(cc, userId, ClassCourseTeacher.PrimaryRole, from);
        if (to is { } end)
        {
            teacher.End(end);
        }

        db.Add(teacher);
        await db.SaveChangesAsync();
        return teacher;
    }

    [Fact]
    public async Task Teacher_filter_contains_only_current_class_courses()
    {
        var w = await SeedAsync("RS1");
        var cls = await AcademicTestKit.AddClassAsync(factory, w.Tree.School1, w.Year);
        var teacher = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher, await UserIdAsync());
        var current = await CourseInAsync(cls);
        var ended = await CourseInAsync(cls);
        var future = await CourseInAsync(cls);
        var someoneElses = await CourseInAsync(cls);
        await TeachAsync(current, teacher.UserId, Today.AddDays(-5));
        await TeachAsync(ended, teacher.UserId, Today.AddDays(-30), Today.AddDays(-1));
        await TeachAsync(future, teacher.UserId, Today.AddDays(1));
        await TeachAsync(someoneElses, await UserIdAsync(), Today.AddDays(-5));
        var (_, scope, db) = ScopeFor(w.Tree, teacher);
        await using var _db = db;

        var filter = await scope.ForAsync(Permissions.Classes.View, default);

        Assert.False(filter.IsUnrestricted);
        Assert.Equal([current.Id], filter.ClassCourseIds);
        Assert.Equal([cls.Id], filter.ClassIds);
        Assert.True(filter.AllowsClassCourse(current.Id));
        Assert.False(filter.AllowsClassCourse(ended.Id));
        Assert.False(filter.AllowsClassCourse(future.Id));
        Assert.False(filter.AllowsClassCourse(someoneElses.Id));
    }

    [Fact]
    public async Task Homeroom_teacher_sees_class_and_its_students()
    {
        var w = await SeedAsync("RS2");
        var teacher = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher, await UserIdAsync());
        var homeroom = await AcademicTestKit.AddClassAsync(factory, w.Tree.School1, w.Year);
        var other = await AcademicTestKit.AddClassAsync(factory, w.Tree.School1, w.Year);
        await using (var write = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var tracked = await write.Set<Class>().SingleAsync(c => c.Id == homeroom.Id);
            tracked.Update(tracked.Code, tracked.NameAr, tracked.NameEn, tracked.Capacity, teacher.UserId);
            await write.SaveChangesAsync();
        }

        var mine = await PeopleTestKit.AddStudentAsync(factory, w.Tree.School1, PeopleTestKit.NewCode());
        var theirs = await PeopleTestKit.AddStudentAsync(factory, w.Tree.School1, PeopleTestKit.NewCode());
        await AcademicTestKit.PlaceAsync(factory, homeroom, mine);
        await AcademicTestKit.PlaceAsync(factory, other, theirs);
        var (_, scope, db) = ScopeFor(w.Tree, teacher);
        await using var _db = db;

        var filter = await scope.ForAsync(Permissions.Students.View, default);
        var visible = await filter.Students(db.Set<StudentProfile>(), db).Select(s => s.Id).ToListAsync();

        Assert.True(filter.AllowsClass(homeroom.Id));
        Assert.False(filter.AllowsClass(other.Id));
        Assert.Equal([homeroom.Id], filter.HomeroomClassIds);
        Assert.Equal([mine.Id], visible);
    }

    [Fact]
    public async Task Supervisor_filter_contains_only_supervised_courses()
    {
        var w = await SeedAsync("RS3");
        var supervisor = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Supervisor, await UserIdAsync());
        var supervised = await AcademicTestKit.AddCourseAsync(factory, w.Tree.School1);
        var other = await AcademicTestKit.AddCourseAsync(factory, w.Tree.School1);
        var cls = await AcademicTestKit.AddClassAsync(factory, w.Tree.School1, w.Year);
        var taught = await AcademicTestKit.AddClassCourseAsync(factory, cls, supervised);
        var untouched = await AcademicTestKit.AddClassCourseAsync(factory, cls, other);
        await using (var write = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            write.Add(new CourseSupervisor(supervised, supervisor.UserId));
            await write.SaveChangesAsync();
        }

        var (_, scope, db) = ScopeFor(w.Tree, supervisor);
        await using var _db = db;

        var filter = await scope.ForAsync(Permissions.Courses.Update, default);
        var unrelated = await scope.ForAsync(Permissions.Courses.View, default); // the catalog stays unit-wide

        Assert.Equal([supervised.Id], filter.CourseIds);
        Assert.Equal([taught.Id], filter.ClassCourseIds);
        Assert.True(filter.AllowsCourse(supervised.Id));
        Assert.False(filter.AllowsCourse(other.Id));
        Assert.False(filter.AllowsClassCourse(untouched.Id));
        Assert.True(unrelated.IsUnrestricted);
    }

    [Fact]
    public async Task OrgAdmin_and_custom_role_are_unrestricted()
    {
        var w = await SeedAsync("RS4");
        var admin = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        Role custom;
        await using (var write = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            custom = new Role(w.Tree.Org.Id, null, "مخصص", "Custom", RoleBaseKind.Staff);
            write.Add(custom);
            await write.SaveChangesAsync();
        }

        RoleAssignment customHolder;
        await using (var write = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            customHolder = new RoleAssignment(w.Tree.School1, Guid.NewGuid(), custom.Id);
            write.Add(customHolder);
            await write.SaveChangesAsync();
        }

        foreach (var holder in new[] { admin, customHolder })
        {
            var (_, scope, db) = ScopeFor(w.Tree, holder);
            await using var _db = db;
            Assert.True((await scope.ForAsync(Permissions.Students.View, default)).IsUnrestricted);
            Assert.True((await scope.ForAsync(Permissions.Courses.Update, default)).IsUnrestricted);
        }

        var (_, platformScope, platformDb) = ScopeFor(w.Tree, admin, platform: true);
        await using var _pdb = platformDb;
        Assert.True((await platformScope.ForAsync(Permissions.Students.View, default)).IsUnrestricted);
    }

    [Fact]
    public async Task Student_filter_is_own_enrollments()
    {
        var w = await SeedAsync("RS5");
        var userId = Guid.NewGuid();
        var student = await PeopleTestKit.AddStudentAsync(factory, w.Tree.School1, PeopleTestKit.NewCode());
        await using (var write = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var tracked = await write.Set<StudentProfile>().SingleAsync(s => s.Id == student.Id);
            tracked.LinkUser(userId);
            await write.SaveChangesAsync();
        }

        var assignment = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Student, userId);
        var cls = await AcademicTestKit.AddClassAsync(factory, w.Tree.School1, w.Year);
        var active = await CourseInAsync(cls);
        var dropped = await CourseInAsync(cls);
        var notMine = await CourseInAsync(cls);
        var placement = await AcademicTestKit.PlaceAsync(factory, cls, student);
        var stranger = await PeopleTestKit.AddStudentAsync(factory, w.Tree.School1, PeopleTestKit.NewCode());
        var strangerPlacement = await AcademicTestKit.PlaceAsync(factory, await AcademicTestKit.AddClassAsync(factory, w.Tree.School1, w.Year), stranger);
        await using (var write = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var gone = new CourseEnrollment(dropped, placement, "Manual", Today.AddDays(-20));
            gone.Drop(Today.AddDays(-2), "left");
            write.AddRange(new CourseEnrollment(active, placement, "Auto", Today.AddDays(-20)), gone,
                new CourseEnrollment(notMine, strangerPlacement, "Auto", Today.AddDays(-20)));
            await write.SaveChangesAsync();
        }

        var (_, scope, db) = ScopeFor(w.Tree, assignment);
        await using var _db = db;

        var filter = await scope.ForAsync(Permissions.LessonPlans.View, default);

        Assert.Equal([active.Id], filter.ClassCourseIds);
        Assert.False(filter.AllowsClassCourse(dropped.Id));
        Assert.False(filter.AllowsClassCourse(notMine.Id));
    }

    [Fact]
    public async Task Check_class_course_returns_404_for_foreign_class_course_of_same_school()
    {
        var w = await SeedAsync("RS6");
        var cls = await AcademicTestKit.AddClassAsync(factory, w.Tree.School1, w.Year);
        var teacher = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher, await UserIdAsync());
        var admin = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var own = await CourseInAsync(cls);
        var foreign = await CourseInAsync(cls);
        await TeachAsync(own, teacher.UserId, Today.AddDays(-1));
        await TeachAsync(foreign, await UserIdAsync(), Today.AddDays(-1));

        Task<HttpResponseMessage> Get(RoleAssignment caller, Guid id)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/test/relationship/class-course/{id}");
            request.Headers.Add(TestAuthHandler.UserHeader, caller.UserId.ToString());
            request.Headers.Add(ContextHeader.Name, caller.Id.ToString());
            return factory.CreateClient().SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.OK, (await Get(teacher, own.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(teacher, foreign.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(teacher, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get(admin, foreign.Id)).StatusCode);
    }

    [Fact]
    public async Task Filter_is_computed_once_per_request()
    {
        var w = await SeedAsync("RS7");
        var teacher = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher, await UserIdAsync());
        var (_, scope, db) = ScopeFor(w.Tree, teacher);
        await using var _db = db;

        var first = await scope.ForAsync(Permissions.Classes.View, default);
        var second = await scope.ForAsync(Permissions.Students.View, default); // same relationship, other permission

        Assert.Same(first, second);
    }

    private Task<HttpResponseMessage> GetAsync(RoleAssignment caller, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(TestAuthHandler.UserHeader, caller.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, caller.Id.ToString());
        return factory.CreateClient().SendAsync(request);
    }

    private async Task EnrollAsync(ClassCourse cc, ClassStudent placement)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new CourseEnrollment(cc, placement, "Manual", Today.AddDays(-3)));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Check_course_404_for_unsupervised_course_of_same_unit()
    {
        var w = await SeedAsync("RS8");
        var supervisor = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Supervisor, await UserIdAsync());
        var admin = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, await UserIdAsync());
        var supervised = await AcademicTestKit.AddCourseAsync(factory, w.Tree.School1);
        var other = await AcademicTestKit.AddCourseAsync(factory, w.Tree.School1);
        await using (var write = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            write.Add(new CourseSupervisor(supervised, supervisor.UserId));
            await write.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(supervisor, $"/test/relationship/course/{supervised.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(supervisor, $"/test/relationship/course/{other.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(supervisor, $"/test/relationship/course/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(admin, $"/test/relationship/course/{supervised.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(admin, $"/test/relationship/course/{other.Id}")).StatusCode);
    }

    [Fact]
    public async Task Check_class_404_for_other_class_of_same_school()
    {
        var w = await SeedAsync("RS9");
        var teacher = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher, await UserIdAsync());
        var mine = await AcademicTestKit.AddClassAsync(factory, w.Tree.School1, w.Year);
        var other = await AcademicTestKit.AddClassAsync(factory, w.Tree.School1, w.Year);
        await TeachAsync(await CourseInAsync(mine), teacher.UserId, Today.AddDays(-1));

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(teacher, $"/test/relationship/class/{mine.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(teacher, $"/test/relationship/class/{other.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(teacher, $"/test/relationship/class/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Check_student_reaches_enrolled_and_homeroom_students_only()
    {
        var w = await SeedAsync("RS10");
        var teacher = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher, await UserIdAsync());
        var teaching = await AcademicTestKit.AddClassAsync(factory, w.Tree.School1, w.Year);
        var homeroom = await AcademicTestKit.AddClassAsync(factory, w.Tree.School1, w.Year);
        await using (var write = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var tracked = await write.Set<Class>().SingleAsync(c => c.Id == homeroom.Id);
            tracked.Update(tracked.Code, tracked.NameAr, tracked.NameEn, tracked.Capacity, teacher.UserId);
            await write.SaveChangesAsync();
        }

        var optionalCourse = await CourseInAsync(teaching);
        await TeachAsync(optionalCourse, teacher.UserId, Today.AddDays(-1));
        var enrolled = await PeopleTestKit.AddStudentAsync(factory, w.Tree.School1, PeopleTestKit.NewCode());
        var sameClassNotEnrolled = await PeopleTestKit.AddStudentAsync(factory, w.Tree.School1, PeopleTestKit.NewCode());
        var homeroomStudent = await PeopleTestKit.AddStudentAsync(factory, w.Tree.School1, PeopleTestKit.NewCode());
        var unrelated = await PeopleTestKit.AddStudentAsync(factory, w.Tree.School1, PeopleTestKit.NewCode());
        await EnrollAsync(optionalCourse, await AcademicTestKit.PlaceAsync(factory, teaching, enrolled));
        await AcademicTestKit.PlaceAsync(factory, teaching, sameClassNotEnrolled);
        await AcademicTestKit.PlaceAsync(factory, homeroom, homeroomStudent);

        string Url(Guid id) => $"/test/relationship/student/{id}";
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(teacher, Url(enrolled.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(teacher, Url(homeroomStudent.Id))).StatusCode);
        // In the class of a course the teacher teaches but not enrolled in it (D42): not reachable.
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(teacher, Url(sameClassNotEnrolled.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(teacher, Url(unrelated.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(teacher, Url(Guid.NewGuid()))).StatusCode);

        var (_, scope, db) = ScopeFor(w.Tree, teacher);
        await using var _db = db;
        var filter = await scope.ForAsync(Permissions.Students.View, default);
        var visible = await filter.Students(db.Set<StudentProfile>(), db).Select(s => s.Id).ToListAsync();
        Assert.Equal(new[] { enrolled.Id, homeroomStudent.Id }.Order(), visible.Order());
    }
}
