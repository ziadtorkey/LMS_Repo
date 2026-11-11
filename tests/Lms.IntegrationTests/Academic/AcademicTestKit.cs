using Lms.Domain.AcademicYears;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;

namespace Lms.IntegrationTests.Academic;

/// <summary>Every academic row created at one unit, so isolation tests can compare units and clients.</summary>
internal sealed record AcademicRows(
    AcademicYear Year, Course Course, CourseAllocation Allocation, CourseRestriction Restriction, CourseSupervisor Supervisor,
    Class Class, Class OtherClass, StudentProfile Student, ClassStudent Placement, ClassCourse ClassCourse,
    TeacherCourseEligibility Eligibility, ClassCourseTeacher Teacher, CourseEnrollment Enrollment, StudentGroup Group,
    GroupMember Member, StudentTransfer Transfer);

internal static class AcademicTestKit
{
    private static readonly DateOnly Start = new(2041, 9, 1);

    public static string NewCode() => "C" + Guid.NewGuid().ToString("N")[..10];

    public static async Task<AcademicYear> AddYearAsync(LmsApiFactory factory, OrgUnit unit)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var year = new AcademicYear(unit, "عام", "Year " + unit.Code, Start, Start.AddMonths(10));
        db.Add(year);
        await db.SaveChangesAsync();
        return year;
    }

    public static async Task<Course> AddCourseAsync(LmsApiFactory factory, OrgUnit owner, string? code = null)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var course = new Course(owner, code ?? NewCode(), "مادة", "Course", null, null, "en", 4);
        db.Add(course);
        await db.SaveChangesAsync();
        return course;
    }

    public static async Task<CourseAllocation> AllocateAsync(LmsApiFactory factory, Course course, OrgUnit unit)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var allocation = new CourseAllocation(course, unit);
        db.Add(allocation);
        await db.SaveChangesAsync();
        return allocation;
    }

    public static async Task<Class> AddClassAsync(LmsApiFactory factory, OrgUnit unit, AcademicYear year, string? code = null)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var cls = new Class(unit, year.Id, code ?? NewCode(), "فصل", "Class", 30);
        db.Add(cls);
        await db.SaveChangesAsync();
        return cls;
    }

    public static async Task<ClassCourse> AddClassCourseAsync(LmsApiFactory factory, Class cls, Course course)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var classCourse = new ClassCourse(cls, course, false);
        db.Add(classCourse);
        await db.SaveChangesAsync();
        return classCourse;
    }

    public static async Task<ClassStudent> PlaceAsync(LmsApiFactory factory, Class cls, StudentProfile student)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var placement = new ClassStudent(cls, student.Id, Start);
        db.Add(placement);
        await db.SaveChangesAsync();
        return placement;
    }

    /// <summary>One row of every academic table at <paramref name="unit"/> (the course is owned and allocated there).</summary>
    public static async Task<AcademicRows> AddAllAsync(LmsApiFactory factory, OrgUnit unit)
    {
        var year = await AddYearAsync(factory, unit);
        var course = await AddCourseAsync(factory, unit);
        var student = await PeopleTestKit.AddStudentAsync(factory, unit, PeopleTestKit.NewCode());
        var teacher = await PeopleTestKit.AddUserAsync(factory);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var allocation = new CourseAllocation(course, unit);
        var restriction = new CourseRestriction(course, CourseRestrictionEvaluator.Gender, ["female"]);
        var supervisor = new CourseSupervisor(course, teacher.Id);
        var cls = new Class(unit, year.Id, NewCode(), "فصل", "Class", 30);
        var other = new Class(unit, year.Id, NewCode(), "فصل ٢", "Class 2", 30);
        var placement = new ClassStudent(cls, student.Id, Start);
        var classCourse = new ClassCourse(cls, course, false);
        var eligibility = new TeacherCourseEligibility(unit, teacher.Id, course);
        var assigned = new ClassCourseTeacher(classCourse, teacher.Id, ClassCourseTeacher.PrimaryRole, Start);
        var enrollment = new CourseEnrollment(classCourse, placement, "Auto", Start);
        var group = new StudentGroup(classCourse, "مجموعة", "Group", null, null);
        var member = new GroupMember(group, enrollment);
        var transfer = new StudentTransfer(student.Id, cls, other, Start, "test", null, DateTime.UtcNow);
        db.AddRange(allocation, restriction, supervisor, cls, other, placement, classCourse, eligibility, assigned, enrollment, group, member, transfer);
        await db.SaveChangesAsync();
        return new AcademicRows(
            year, course, allocation, restriction, supervisor, cls, other, student, placement, classCourse, eligibility, assigned,
            enrollment, group, member, transfer);
    }
}
