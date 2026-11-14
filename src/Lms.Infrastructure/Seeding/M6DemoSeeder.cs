using Lms.Domain.AcademicYears;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Seeding;

/// <summary>
/// The people of the M6 messaging demo and its E2E (<c>MessagingWorld</c>, M6 Task 18), in the school client only (Development, idempotent, keyed by the
/// class code, one transaction). <c>orgadmin</c>, <c>manager</c> and <c>teacher</c> are the sample accounts of <see cref="DevSeeder"/>; this adds
/// <c>student1</c> and <c>student2</c> (class 10-A, taught by <c>teacher</c>, in groups 1 and 2 of one class course) and <c>guardian1</c> and
/// <c>guardian2</c> (guardian of student 1 only, of student 2 only), so the recipient rules (D69) have a stranger of every kind. No secrets: only names.
/// </summary>
internal static class M6DemoSeeder
{
    public const string ClassCode = "DEMO-M6-10A";
    public const string Domain = "altagamo3.test";

    public static string Email(string localPart) => $"{localPart}@{Domain}";

    public static async Task SeedAsync(LmsDbContext db, DevSeeder.SeededClient school, string password, CancellationToken ct)
    {
        if (await db.Set<Class>().AnyAsync(c => c.OrganizationId == school.Organization.Id && c.CodeKey == ClassCode, ct))
        {
            return;
        }

        var teacher = await DevSeeder.UserIdAsync(db, Email("teacher"), ct);
        if (teacher is null)
        {
            return; // no sample accounts (no seed password): nothing to attach the world to
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var roles = await db.Roles.Where(r => r.OrganizationId == school.Organization.Id && r.BuiltInKey != null).ToDictionaryAsync(r => r.BuiltInKey!, ct);
        var yearUnit = school.Units["S"];
        var classUnit = school.Units["KG-S1"];

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startYear = today.Month >= 9 ? today.Year : today.Year - 1;
        var year = await db.Set<AcademicYear>().FirstOrDefaultAsync(y => y.OrgUnitId == yearUnit.Id && y.Status == AcademicYearStatus.Active, ct);
        if (year is null)
        {
            year = new AcademicYear(yearUnit, $"العام الدراسي {startYear}/{startYear + 1} (رسائل)", $"School year {startYear}/{startYear + 1} (messaging demo)",
                new DateOnly(startYear, 9, 1), new DateOnly(startYear + 1, 6, 30));
            year.Activate();
            db.Add(year);
            await db.SaveChangesAsync(ct);
        }

        var cls = new Class(classUnit, year.Id, ClassCode, "الصف 10-أ", "Class 10-A", 30);
        var course = new Course(yearUnit, "DEMO-M6-ENG", "اللغة الإنجليزية (رسائل)", "English (messaging demo)", null, null, "ar", 4);
        db.AddRange(cls, course);
        await db.SaveChangesAsync(ct);
        var classCourse = new ClassCourse(cls, course, false);
        db.AddRange(classCourse, new CourseAllocation(course, classUnit));
        await db.SaveChangesAsync(ct);
        db.AddRange(
            new ClassCourseTeacher(classCourse, teacher.Value, ClassCourseTeacher.PrimaryRole, year.StartDate),
            new TeacherCourseEligibility(classUnit, teacher.Value, course));

        var group1 = new StudentGroup(classCourse, "المجموعة 1", "Group 1", null, null);
        var group2 = new StudentGroup(classCourse, "المجموعة 2", "Group 2", null, null);
        db.AddRange(group1, group2);
        await db.SaveChangesAsync(ct);

        await AddFamilyAsync(db, school, roles, password, "student1", "guardian1", ("طالب ١", "Student One"), ("ولي أمر ١", "Guardian One"), cls, classCourse, group1, year.StartDate, ct);
        await AddFamilyAsync(db, school, roles, password, "student2", "guardian2", ("طالب ٢", "Student Two"), ("ولي أمر ٢", "Guardian Two"), cls, classCourse, group2, year.StartDate, ct);
        await tx.CommitAsync(ct);
    }

    private static async Task AddFamilyAsync(
        LmsDbContext db, DevSeeder.SeededClient school, Dictionary<string, Role> roles, string password, string studentName, string guardianName,
        (string Ar, string En) student, (string Ar, string En) guardian, Class cls, ClassCourse classCourse, StudentGroup group, DateOnly from, CancellationToken ct)
    {
        var studentUser = await DevSeeder.EnsureUserAsync(db, Email(studentName), student.Ar, student.En, password, false, ct);
        var guardianUser = await DevSeeder.EnsureUserAsync(db, Email(guardianName), guardian.Ar, guardian.En, password, false, ct);
        db.AddRange(
            new RoleAssignment(school.Units["KG-S1"], studentUser.Id, roles["Student"].Id),
            new RoleAssignment(school.Units["S"], guardianUser.Id, roles["Guardian"].Id));

        var profile = new StudentProfile(school.Units["KG-S1"], "DEMO-M6-" + studentName.ToUpperInvariant(), student.Ar, student.En, new DateOnly(from.Year - 15, 5, 1), "male", "muslim", "SA");
        profile.LinkUser(studentUser.Id);
        var guardianProfile = new GuardianProfile(school.Organization.Id, guardian.Ar, guardian.En, "05000000" + (studentName.EndsWith('1') ? "11" : "22"), null, null);
        guardianProfile.LinkUser(guardianUser.Id);
        db.AddRange(profile, guardianProfile);
        await db.SaveChangesAsync(ct);

        var placement = new ClassStudent(cls, profile.Id, from);
        var enrollment = new CourseEnrollment(classCourse, placement, "Auto", from);
        db.AddRange(placement, enrollment, StudentGuardian.For(profile, guardianProfile, "Father", true));
        await db.SaveChangesAsync(ct);
        db.Add(new GroupMember(group, enrollment));
        await db.SaveChangesAsync(ct);
    }
}
