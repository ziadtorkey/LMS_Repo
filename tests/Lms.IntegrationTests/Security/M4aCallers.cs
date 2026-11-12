using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The callers whose answers depend on a ClassCourse, a group or a relationship in M4a (teaching and learning; M4a plan Task 29,
/// PRD §3.3 step 2, D42, D51). Admins of School1, School2 and client B come from the M1 callers of the same world
/// (<see cref="M4aCallers.Callers"/>).
/// </summary>
public enum M4aCaller
{
    /// <summary>Teacher at School1 who teaches class course X (course CX in class 1).</summary>
    TeacherA,

    /// <summary>Teacher at School1 who teaches class course Y (class 2): a colleague with no access to X.</summary>
    TeacherB,

    /// <summary>Student enrolled in X, member of group 1 of X.</summary>
    StudentGroup1,

    /// <summary>Student enrolled in X, member of group 2 of X (same class course, other group).</summary>
    StudentGroup2,

    /// <summary>Student of class 2: enrolled in Y, not in X at all.</summary>
    StudentOfOtherClass,

    /// <summary>Guardian of the group 1 student: no M4a access (grades arrive in M5).</summary>
    Guardian,
}

/// <summary>
/// The M4a probe world: the <see cref="M3Callers"/> school (School1 of the M1 probe world, classes 1 and 2, class courses X and Y,
/// teachers A and B) plus a second student in X and two groups of X, built straight in the database so it depends on no M4a
/// route: group 1 holds the student of the M3 world, group 2 the new student (D6: one group per student and class course).
/// </summary>
public sealed class M4aCallers
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static M4aCallers? _shared;
    private readonly Dictionary<M4aCaller, RoleAssignment> _assignments;

    private M4aCallers(M3Callers m3, Dictionary<M4aCaller, RoleAssignment> assignments, M4aData data)
    {
        M3 = m3;
        _assignments = assignments;
        Data = data;
    }

    /// <summary>The M3 world: its M1 probe world is what the probe rows' builders target.</summary>
    public M3Callers M3 { get; }

    public M1ProbeWorld World => M3.World;

    public M1Callers Callers => M3.Callers;

    public M4aData Data { get; }

    /// <summary>The M3 class course X (class 1) and Y (class 2), the two groups of X and the students.</summary>
    public sealed record M4aData(
        ClassCourse ClassCourseX, ClassCourse ClassCourseY, StudentGroup Group1, StudentGroup Group2,
        StudentProfile StudentGroup1, StudentProfile StudentGroup2, StudentProfile StudentOfOtherClass);

    public static async Task<M4aCallers> GetAsync(LmsApiFactory factory)
    {
        await Gate.WaitAsync();
        try
        {
            if (_shared is null || !ReferenceEquals(_shared.Callers.Factory, factory))
            {
                _shared = await BuildAsync(factory);
            }

            return _shared;
        }
        finally
        {
            Gate.Release();
        }
    }

    public RoleAssignment AssignmentOf(M4aCaller caller) => _assignments[caller];

    public Task<HttpResponseMessage> SendAsync(M4aCaller caller, HttpMethod method, string url, object? body = null) =>
        Callers.SendAsAsync(_assignments[caller], method, url, body);

    private static async Task<M4aCallers> BuildAsync(LmsApiFactory factory)
    {
        var m3 = await M3Callers.GetAsync(factory);
        var tree = m3.Callers.A;
        var school1 = tree.School1;
        var data = m3.Data;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // The M3 student 1 already has a user and the guardian 1 a user; student 2 (class 2) gets one here, and a second student
        // joins class 1 and course X.
        var student2User = await PeopleTestKit.AddUserAsync(factory);
        var extra = await PeopleTestKit.AddStudentAsync(factory, school1, PeopleTestKit.NewCode());
        var extraUser = await PeopleTestKit.AddUserAsync(factory);

        StudentGroup group1, group2;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<StudentProfile>().SingleAsync(s => s.Id == data.Student2.Id)).LinkUser(student2User.Id);
            (await db.Set<StudentProfile>().SingleAsync(s => s.Id == extra.Id)).LinkUser(extraUser.Id);
            var class1 = await db.Set<Class>().SingleAsync(c => c.Id == data.Class1.Id);
            var classCourseX = await db.Set<ClassCourse>().SingleAsync(c => c.Id == data.ClassCourseX.Id);
            var placement = new ClassStudent(class1, extra.Id, today.AddDays(-30));
            var enrollment = new CourseEnrollment(classCourseX, placement, "Auto", today.AddDays(-30));
            var student1Enrollment = await db.Set<CourseEnrollment>()
                .SingleAsync(e => e.ClassCourseId == classCourseX.Id && e.StudentId == data.Student1.Id);
            group1 = new StudentGroup(classCourseX, "مجموعة ١", "Group 1", null, null);
            group2 = new StudentGroup(classCourseX, "مجموعة ٢", "Group 2", null, null);
            db.AddRange(placement, enrollment, group1, group2);
            await db.SaveChangesAsync();
            db.AddRange(new GroupMember(group1, student1Enrollment), new GroupMember(group2, enrollment));
            await db.SaveChangesAsync();
        }

        var teacherA = m3.AssignmentOf(M3Caller.TeacherA);
        var teacherB = m3.AssignmentOf(M3Caller.TeacherB);
        var assignments = new Dictionary<M4aCaller, RoleAssignment>
        {
            [M4aCaller.TeacherA] = teacherA,
            [M4aCaller.TeacherB] = teacherB,
            [M4aCaller.StudentGroup1] = m3.AssignmentOf(M3Caller.Student1),
            [M4aCaller.StudentGroup2] = await AccessTestData.AssignAsync(factory, tree, school1, BuiltInRoles.Student, extraUser.Id),
            [M4aCaller.StudentOfOtherClass] = await AccessTestData.AssignAsync(factory, tree, school1, BuiltInRoles.Student, student2User.Id),
            [M4aCaller.Guardian] = m3.AssignmentOf(M3Caller.Guardian1),
        };

        using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var x = await read.Set<ClassCourse>().SingleAsync(c => c.Id == data.ClassCourseX.Id);
        var y = await read.Set<ClassCourse>().SingleAsync(c => c.Id == data.ClassCourseY.Id);
        return new M4aCallers(
            m3, assignments, new M4aData(x, y, group1, group2, data.Student1, extra, data.Student2));
    }
}
