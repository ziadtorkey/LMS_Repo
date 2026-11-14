using Lms.Domain.AcademicYears;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Grading;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Grading.Scales;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Seeding;

/// <summary>
/// Demo data of milestone 5 for the two sample clients (Development only, idempotent: each client's demo is created once, keyed by its class
/// code, in one transaction): a school year with two terms, the Saudi 5-band school scale, a class with five students, a gradebook with
/// published and unpublished grades; a university year with the 5.00 GPA scale and a small gradebook; then the timetable and attendance
/// (<see cref="M5DemoTimetableSeeder"/>). No secrets: only names, dates and numbers. Students, guardian and teacher accounts are linked when the
/// accounts exist (they exist only when a seed password is configured).
/// </summary>
internal static class M5DemoSeeder
{
    public const string SchoolClassCode = "DEMO-M5-KG1";
    public const string UniversityClassCode = "DEMO-M5-GEN1";

    internal sealed record Demo(
        OrgUnit YearUnit, OrgUnit ClassUnit, AcademicYear Year, Term First, Term Second, Class Class, Course Course, ClassCourse ClassCourse,
        IReadOnlyList<StudentProfile> Students);

    public static async Task SeedAsync(LmsDbContext db, DevSeeder.SeededClient school, DevSeeder.SeededClient university, CancellationToken ct)
    {
        var scales = new GradingScaleService(db, new NoScaleUsage(), new SaudiPresetCatalog());
        await SeedSchoolAsync(db, school, scales, ct);
        await SeedUniversityAsync(db, university, scales, ct);
    }

    private static async Task SeedSchoolAsync(LmsDbContext db, DevSeeder.SeededClient school, GradingScaleService scales, CancellationToken ct)
    {
        if (await db.Set<Class>().AnyAsync(c => c.OrganizationId == school.Organization.Id && c.CodeKey == SchoolClassCode, ct))
        {
            return;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var scale = await ConfirmedScaleAsync(db, scales, school.Units["S"], SaudiPresetSeed.SchoolKey, ct);
        var demo = await WorldAsync(
            db, school, "S", "KG-S1", SchoolClassCode, "DEMO-MATH", "الرياضيات", "Mathematics",
            [("DEMO-S01", "سارة", "Sara"), ("DEMO-S02", "خالد", "Khalid"), ("DEMO-S03", "نورة", "Noura"), ("DEMO-S04", "فهد", "Fahad"), ("DEMO-S05", "ريم", "Reem")],
            ct);
        await LinkAccountsAsync(db, demo, school.Organization.Id, ct);
        await GradebookAsync(db, demo, scale, ct);
        await M5DemoTimetableSeeder.SeedAsync(db, school, demo, ct);
        await tx.CommitAsync(ct);
    }

    private static async Task SeedUniversityAsync(LmsDbContext db, DevSeeder.SeededClient university, GradingScaleService scales, CancellationToken ct)
    {
        if (await db.Set<Class>().AnyAsync(c => c.OrganizationId == university.Organization.Id && c.CodeKey == UniversityClassCode, ct))
        {
            return;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var scale = await ConfirmedScaleAsync(db, scales, university.Units["MNF"], SaudiPresetSeed.University5Key, ct);
        var demo = await WorldAsync(
            db, university, "FCI", "FCI-1-GEN", UniversityClassCode, "DEMO-CS101", "مقدمة في البرمجة", "Introduction to Programming",
            [("DEMO-U01", "عبدالله", "Abdullah"), ("DEMO-U02", "لينا", "Lina"), ("DEMO-U03", "ماجد", "Majed")], ct);
        await GradebookAsync(db, demo, scale, ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>The preset copy at the unit, confirmed so publishing needs no acknowledgement; reused when it already exists.</summary>
    private static async Task<GradingScale> ConfirmedScaleAsync(
        LmsDbContext db, GradingScaleService scales, OrgUnit unit, string seedKey, CancellationToken ct)
    {
        var existing = await db.Set<GradingScale>().FirstOrDefaultAsync(s => s.OrgUnitId == unit.Id && s.SeedKey == seedKey && !s.IsTemplate, ct);
        var scale = existing ?? await scales.CopyPresetAsync(unit, seedKey, ct) ?? throw new InvalidOperationException($"Unknown preset {seedKey}.");
        if (scale.Provisional)
        {
            await scales.ConfirmAsync(scale, ct);
        }

        return scale;
    }

    private static async Task<Demo> WorldAsync(
        LmsDbContext db, DevSeeder.SeededClient client, string yearUnitCode, string classUnitCode, string classCode, string courseCode,
        string courseNameAr, string courseNameEn, (string Code, string NameAr, string NameEn)[] people, CancellationToken ct)
    {
        var yearUnit = client.Units[yearUnitCode];
        var classUnit = client.Units[classUnitCode];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startYear = today.Month >= 9 ? today.Year : today.Year - 1;
        var start = new DateOnly(startYear, 9, 1);
        var end = new DateOnly(startYear + 1, 6, 30);

        var year = new AcademicYear(yearUnit, $"العام الدراسي {startYear}/{startYear + 1} (عرض)", $"School year {startYear}/{startYear + 1} (demo)", start, end);
        year.Activate();
        var first = new Term(year, "الفصل الأول", "First term", start, new DateOnly(startYear + 1, 1, 31));
        var second = new Term(year, "الفصل الثاني", "Second term", new DateOnly(startYear + 1, 2, 1), end);
        first.SetOrder(1);
        second.SetOrder(2);
        var cls = new Class(classUnit, year.Id, classCode, "فصل العرض", "Demo class", 30);
        var course = new Course(yearUnit, courseCode, courseNameAr, courseNameEn, null, null, "ar", 5);
        db.AddRange(year, first, second, cls, course);
        await db.SaveChangesAsync(ct);

        var classCourse = new ClassCourse(cls, course, false);
        db.AddRange(classCourse, new CourseAllocation(course, classUnit));
        var students = new List<StudentProfile>();
        foreach (var (code, nameAr, nameEn) in people)
        {
            var student = new StudentProfile(classUnit, code, nameAr, nameEn, new DateOnly(startYear - 8, 3, 15), "female", "muslim", "SA");
            students.Add(student);
        }

        db.AddRange(students);
        await db.SaveChangesAsync(ct);
        foreach (var student in students)
        {
            var placement = new ClassStudent(cls, student.Id, start);
            db.AddRange(placement, new CourseEnrollment(classCourse, placement, "Auto", start));
        }

        await db.SaveChangesAsync(ct);
        return new Demo(yearUnit, classUnit, year, first, second, cls, course, classCourse, students);
    }

    /// <summary>Links the sample student, guardian and teacher accounts (school only) to the demo class when they exist.</summary>
    private static async Task LinkAccountsAsync(LmsDbContext db, Demo demo, Guid organizationId, CancellationToken ct)
    {
        var teacher = await UserIdAsync(db, "teacher@altagamo3.test", ct);
        if (teacher is { } teacherId)
        {
            db.AddRange(
                new ClassCourseTeacher(demo.ClassCourse, teacherId, ClassCourseTeacher.PrimaryRole, demo.Year.StartDate),
                new TeacherCourseEligibility(demo.ClassUnit, teacherId, demo.Course));
        }

        if (await UserIdAsync(db, "student@altagamo3.test", ct) is { } studentUser)
        {
            demo.Students[0].LinkUser(studentUser);
        }

        if (await UserIdAsync(db, "guardian@altagamo3.test", ct) is { } guardianUser)
        {
            var guardian = new GuardianProfile(organizationId, "ولي أمر سارة", "Sara's parent", "0500000001", null, null);
            guardian.LinkUser(guardianUser);
            db.Add(guardian);
            await db.SaveChangesAsync(ct);
            db.Add(StudentGuardian.For(demo.Students[0], guardian, "Father", true));
        }

        await db.SaveChangesAsync(ct);
    }

    internal static async Task<Guid?> UserIdAsync(LmsDbContext db, string email, CancellationToken ct) =>
        (await db.Users.Where(u => u.NormalizedEmail == email.ToUpperInvariant()).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct));

    private static async Task GradebookAsync(LmsDbContext db, Demo demo, GradingScale scale, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var book = new Gradebook(demo.ClassCourse, demo.First.Id, scale.Id, 2, "PointsWeighted", "Normalise");
        var plain = new GradeCategory(book, "غير مصنف", "Uncategorised", 0m, 0, isUncategorised: true);
        var homework = new GradeCategory(book, "الواجبات", "Homework", 30m, 1);
        var quizzes = new GradeCategory(book, "الاختبارات القصيرة", "Quizzes", 30m, 2);
        var exams = new GradeCategory(book, "الاختبارات النهائية", "Exams", 40m, 3);
        var hw1 = GradeItem.Manual(book, homework, "الواجب 1", "Homework 1", 10m, 1);
        var hw2 = GradeItem.Manual(book, homework, "الواجب 2", "Homework 2", 10m, 2);
        var quiz1 = GradeItem.Manual(book, quizzes, "اختبار قصير 1", "Quiz 1", 20m, 3);
        var quiz2 = GradeItem.Manual(book, quizzes, "اختبار قصير 2", "Quiz 2", 20m, 4);
        var midterm = GradeItem.Manual(book, exams, "الاختبار النصفي", "Midterm", 50m, 5);
        hw1.Publish(now);
        hw2.Publish(now);
        quiz1.Publish(now);           // quiz 2 stays hidden, the midterm is scheduled: the demo shows all three publish states
        midterm.Schedule(now.AddDays(7));
        db.AddRange(book, plain, homework, quizzes, exams, hw1, hw2, quiz1, quiz2, midterm);
        await db.SaveChangesAsync(ct);

        // Points per student: [hw1, hw2, quiz1, quiz2, midterm]; null = not graded yet.
        decimal?[][] scores =
        [
            [10, 9, 18, 19, 46], [8, 7, 12, 14, 38], [9.5m, 10, 20, 17, 49], [6, null, 9, 11, 30], [7, 8, 15, 12, null],
        ];
        var items = new[] { hw1, hw2, quiz1, quiz2, midterm };
        for (var s = 0; s < demo.Students.Count; s++)
        {
            for (var i = 0; i < items.Length; i++)
            {
                if (scores[s % scores.Length][i] is not { } points)
                {
                    continue;
                }

                var entry = new GradeEntry(items[i], demo.Students[s].Id, null, now);
                entry.SetPoints(points, null, now);
                db.Add(entry);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
