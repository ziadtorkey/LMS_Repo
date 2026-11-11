using Lms.Domain.AcademicYears;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The callers whose answers depend on a relationship (M3 plan Task 45, PRD §3.3 step 2). Admins, School2 admin, client B
/// and anonymous come from the seven M1 callers of the same world (<see cref="M3Callers.Callers"/>).
/// </summary>
public enum M3Caller
{
    /// <summary>Teacher at School1 teaching class course X (class 1, course CX).</summary>
    TeacherA,

    /// <summary>Teacher at School1 teaching class course Y (class 2, course CY): a colleague with no access to X.</summary>
    TeacherB,

    /// <summary>Supervisor at School1 who supervises course CX (not CY).</summary>
    SupervisorOfCx,

    /// <summary>Supervisor at School1 who supervises course CY (not CX).</summary>
    SupervisorOfCy,

    /// <summary>Student 1: placed in class 1 and enrolled in class course X.</summary>
    Student1,

    /// <summary>Guardian of student 1.</summary>
    Guardian1,
}

/// <summary>
/// One school (School1 of the M1 probe world) with two classes, two courses and the people around them, built straight in the database so it does not
/// depend on any M3 route being merged:
/// class 1 teaches course CX (class course X, teacher A, supervisor of CX, student 1 enrolled, guardian 1), class 2 teaches
/// course CY (class course Y, teacher B, supervisor of CY, student 2 enrolled, guardian 2). Both courses are owned and
/// allocated at School1, so supervision follows the owner unit (D37).
/// </summary>
public sealed class M3Callers
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static M3Callers? _shared;
    private readonly Dictionary<M3Caller, RoleAssignment> _assignments;

    private M3Callers(M1ProbeWorld world, Dictionary<M3Caller, RoleAssignment> assignments, M3Data data)
    {
        World = world;
        Callers = world.Callers;
        _assignments = assignments;
        Data = data;
    }

    /// <summary>The M1 probe world: the M3 people and structure live in client A, so the probe rows' builders target them.</summary>
    public M1ProbeWorld World { get; }

    public M1Callers Callers { get; }

    public M3Data Data { get; }

    public sealed record M3Data(
        AcademicYear Year, Class Class1, Class Class2, Course CourseX, Course CourseY, ClassCourse ClassCourseX, ClassCourse ClassCourseY,
        StudentProfile Student1, StudentProfile Student2, GuardianProfile Guardian1, GuardianProfile Guardian2);

    public static async Task<M3Callers> GetAsync(LmsApiFactory factory)
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

    public RoleAssignment AssignmentOf(M3Caller caller) => _assignments[caller];

    public Task<HttpResponseMessage> SendAsync(M3Caller caller, HttpMethod method, string url, object? body = null) =>
        Callers.SendAsAsync(_assignments[caller], method, url, body);

    private static async Task<M3Callers> BuildAsync(LmsApiFactory factory)
    {
        var world = await M1ProbeWorld.GetAsync(factory);
        var callers = world.Callers;
        var tree = callers.A;
        var school1 = tree.School1;

        var year = await AcademicTestKit.AddYearAsync(factory, tree.Root);
        var courseX = await AcademicTestKit.AddCourseAsync(factory, school1);
        var courseY = await AcademicTestKit.AddCourseAsync(factory, school1);
        await AcademicTestKit.AllocateAsync(factory, courseX, school1);
        await AcademicTestKit.AllocateAsync(factory, courseY, school1);
        var class1 = await AcademicTestKit.AddClassAsync(factory, school1, year);
        var class2 = await AcademicTestKit.AddClassAsync(factory, school1, year);
        var classCourseX = await AcademicTestKit.AddClassCourseAsync(factory, class1, courseX);
        var classCourseY = await AcademicTestKit.AddClassCourseAsync(factory, class2, courseY);

        var student1 = await PeopleTestKit.AddStudentAsync(factory, school1, PeopleTestKit.NewCode());
        var student2 = await PeopleTestKit.AddStudentAsync(factory, school1, PeopleTestKit.NewCode());
        var studentUser = await PeopleTestKit.AddUserAsync(factory);
        var guardianUser = await PeopleTestKit.AddUserAsync(factory);
        var guardian1 = await PeopleTestKit.AddGuardianAsync(factory, tree.Org.Id, PeopleTestKit.NewMobile(), PeopleTestKit.NewNationalId(), guardianUser.Id);
        var guardian2 = await PeopleTestKit.AddGuardianAsync(factory, tree.Org.Id, PeopleTestKit.NewMobile(), PeopleTestKit.NewNationalId());
        var teacherA = await PeopleTestKit.AddUserAsync(factory);
        var teacherB = await PeopleTestKit.AddUserAsync(factory);
        var supervisorX = await PeopleTestKit.AddUserAsync(factory);
        var supervisorY = await PeopleTestKit.AddUserAsync(factory);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<StudentProfile>().SingleAsync(s => s.Id == student1.Id)).LinkUser(studentUser.Id);
            var placement1 = new ClassStudent(class1, student1.Id, today.AddDays(-30));
            var placement2 = new ClassStudent(class2, student2.Id, today.AddDays(-30));
            db.AddRange(
                placement1, placement2,
                new CourseEnrollment(classCourseX, placement1, "Auto", today.AddDays(-30)),
                new CourseEnrollment(classCourseY, placement2, "Auto", today.AddDays(-30)),
                new ClassCourseTeacher(classCourseX, teacherA.Id, ClassCourseTeacher.PrimaryRole, today.AddDays(-30)),
                new ClassCourseTeacher(classCourseY, teacherB.Id, ClassCourseTeacher.PrimaryRole, today.AddDays(-30)),
                new CourseSupervisor(courseX, supervisorX.Id),
                new CourseSupervisor(courseY, supervisorY.Id),
                StudentGuardian.For(student1, guardian1, "father", true),
                StudentGuardian.For(student2, guardian2, "mother", true));
            await db.SaveChangesAsync();
        }

        var assignments = new Dictionary<M3Caller, RoleAssignment>
        {
            [M3Caller.TeacherA] = await AccessTestData.AssignAsync(factory, tree, school1, BuiltInRoles.Teacher, teacherA.Id),
            [M3Caller.TeacherB] = await AccessTestData.AssignAsync(factory, tree, school1, BuiltInRoles.Teacher, teacherB.Id),
            [M3Caller.SupervisorOfCx] = await AccessTestData.AssignAsync(factory, tree, school1, BuiltInRoles.Supervisor, supervisorX.Id),
            [M3Caller.SupervisorOfCy] = await AccessTestData.AssignAsync(factory, tree, school1, BuiltInRoles.Supervisor, supervisorY.Id),
            [M3Caller.Student1] = await AccessTestData.AssignAsync(factory, tree, school1, BuiltInRoles.Student, studentUser.Id),
            [M3Caller.Guardian1] = await AccessTestData.AssignAsync(factory, tree, school1, BuiltInRoles.Guardian, guardianUser.Id),
        };

        return new M3Callers(
            world, assignments,
            new M3Data(year, class1, class2, courseX, courseY, classCourseX, classCourseY, student1, student2, guardian1, guardian2));
    }
}
