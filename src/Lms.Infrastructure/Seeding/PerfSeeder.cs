using Lms.Domain.AcademicYears;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Grading;
using Lms.Domain.Notifications;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Grading.Scales;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Seeding;

/// <summary>
/// A large client for the performance pass (M7 X-09): a school group of up to ten schools with realistic units, classes of about 280 students, six courses
/// per class, one guardian per student, a term gradebook per class course with three published items and a grade per student and item, and a few thousand
/// in-app notifications for the sample accounts. Development only, off unless <c>Seed:Perf:Students</c> is set, idempotent (the client is keyed by a fixed
/// id) and fast: the volume tables go through <see cref="BulkInserter"/>. No secrets: generated names and numbers; the four accounts use the dev seed password.
/// Attendance, timetable and messages are added with their milestones (their tables are not on every branch).
/// </summary>
internal static class PerfSeeder
{
    public static readonly Guid OrganizationId = Guid.Parse("018f0000-0000-7000-8000-0000000000f1");
    public const string Domain = "perf.test";
    public const int StudentsPerClass = 280;
    public const int CoursesPerSchool = 6;
    public const int ItemsPerGradebook = 3;
    public const int NotificationsPerAccount = 2_000;

    public sealed record Result(bool Seeded, int Students, int Classes, long Rows);

    public static string Email(string name) => $"{name}@{Domain}";

    public static async Task<Result> SeedAsync(LmsDbContext db, int students, string password, CancellationToken ct)
    {
        if (await db.Organizations.AnyAsync(o => o.Id == OrganizationId, ct))
        {
            return new Result(false, 0, 0, 0);
        }

        var classCount = Math.Max(1, (int)Math.Ceiling(students / (double)StudentsPerClass));
        var schoolCount = Math.Min(10, classCount);
        var plan = Enumerable.Range(0, classCount).Select(i => (School: i % schoolCount, Stage: i / schoolCount % 3, Section: i / (schoolCount * 3))).ToList();
        var client = await DevSeeder.SeedTreeAsync(db, TreeFor(plan, schoolCount), ct);
        var now = DateTime.UtcNow;
        var bulk = new BulkInserter(db, now);

        var roles = await db.Roles.Where(r => r.OrganizationId == OrganizationId && r.BuiltInKey != null).ToDictionaryAsync(r => r.BuiltInKey!, ct);
        var scale = await ConfirmedScaleAsync(db, client.Units["PERF"], ct);
        var first = plan[0];
        var firstClassUnit = client.Units[SectionCode(first)];
        var firstSchool = client.Units[SchoolCode(first.School)];
        var admin = await DevSeeder.EnsureUserAsync(db, Email("perf-admin"), "مدير الأداء", "Perf Admin", password, false, ct);
        var teacher = await DevSeeder.EnsureUserAsync(db, Email("perf-teacher"), "معلم الأداء", "Perf Teacher", password, false, ct);
        var studentUser = await DevSeeder.EnsureUserAsync(db, Email("perf-student"), "طالب الأداء", "Perf Student", password, false, ct);
        var guardianUser = await DevSeeder.EnsureUserAsync(db, Email("perf-guardian"), "ولي أمر الأداء", "Perf Guardian", password, false, ct);
        db.AddRange(
            new RoleAssignment(client.Units["PERF"], admin.Id, roles["OrgAdmin"].Id),
            new RoleAssignment(firstClassUnit, teacher.Id, roles["Teacher"].Id),
            new RoleAssignment(firstClassUnit, studentUser.Id, roles["Student"].Id),
            new RoleAssignment(firstSchool, guardianUser.Id, roles["Guardian"].Id));
        await db.SaveChangesAsync(ct);

        // Years, terms and courses per school
        var years = new List<AcademicYear>();
        var terms = new Dictionary<Guid, Term>();
        var courses = new Dictionary<int, List<Course>>();
        var startYear = DateTime.UtcNow.Month >= 9 ? DateTime.UtcNow.Year : DateTime.UtcNow.Year - 1;
        for (var s = 0; s < schoolCount; s++)
        {
            var unit = client.Units[SchoolCode(s)];
            var year = new AcademicYear(unit, $"العام {startYear}/{startYear + 1} (أداء)", $"Year {startYear}/{startYear + 1} (perf)", new DateOnly(startYear, 9, 1), new DateOnly(startYear + 1, 6, 30));
            year.Activate();
            var term = new Term(year, "الفصل الأول", "First term", year.StartDate, new DateOnly(startYear + 1, 1, 31));
            term.SetOrder(1);
            years.Add(year);
            terms[year.Id] = term;
            courses[s] = Enumerable.Range(1, CoursesPerSchool).Select(n => new Course(unit, $"PERF-{s}-{n}", $"مادة {n}", $"Subject {n}", null, null, "ar", 4)).ToList();
        }

        await bulk.InsertAsync(years, ct);
        await bulk.InsertAsync(terms.Values.ToList(), ct);
        await bulk.InsertAsync(courses.Values.SelectMany(c => c).ToList(), ct);

        var made = 0;
        for (var c = 0; c < plan.Count; c++)
        {
            var year = years[plan[c].School];
            var term = terms[year.Id];
            var unit = client.Units[SectionCode(plan[c])];
            var inClass = Math.Min(StudentsPerClass, students - made);
            made += inClass;
            await SeedClassAsync(db, bulk, now, unit, year, term, courses[plan[c].School], scale, c, inClass, made - inClass, c == 0 ? (teacher.Id, studentUser.Id, guardianUser.Id) : null, ct);
        }

        var accounts = new[] { admin.Id, teacher.Id, studentUser.Id, guardianUser.Id };
        await bulk.InsertAsync(accounts.SelectMany(u => Enumerable.Range(0, NotificationsPerAccount).Select(i => Notification(u, i, now))).ToList(), ct);
        return new Result(true, students, plan.Count, bulk.Inserted);
    }

    private static async Task SeedClassAsync(
        LmsDbContext db, BulkInserter bulk, DateTime now, OrgUnit unit, AcademicYear year, Term term, List<Course> courses, GradingScale scale, int index, int count, int offset,
        (Guid Teacher, Guid Student, Guid Guardian)? accounts, CancellationToken ct)
    {
        var cls = new Class(unit, year.Id, $"PERF-C{index:D4}", $"الصف {index + 1}", $"Class {index + 1}", StudentsPerClass + 20);
        var classCourses = courses.Select(course => new ClassCourse(cls, course, false)).ToList();
        var students = Enumerable.Range(0, count).Select(i => new StudentProfile(unit, $"PERF-{offset + i + 1:D6}", $"طالب {offset + i + 1}", $"Student {offset + i + 1}", new DateOnly(year.StartDate.Year - 12, 1 + i % 12, 1 + i % 27), i % 2 == 0 ? "male" : "female", "muslim", "SA")).ToList();
        var guardians = students.Select((s, i) => new GuardianProfile(unit.OrganizationId, $"ولي أمر {offset + i + 1}", $"Guardian {offset + i + 1}", $"05{(offset + i) % 100_000_000:D8}", null, null)).ToList();
        if (accounts is { } a)
        {
            students[0].LinkUser(a.Student);
            guardians[0].LinkUser(a.Guardian);
        }

        var placements = students.Select(s => new ClassStudent(cls, s.Id, year.StartDate)).ToList();
        var enrollments = classCourses.SelectMany(cc => placements.Select(p => new CourseEnrollment(cc, p, "Auto", year.StartDate))).ToList();
        var books = classCourses.Select(cc => new Gradebook(cc, term.Id, scale.Id, 2, "PointsWeighted", "Normalise")).ToList();
        var categories = books.Select(b => new GradeCategory(b, "غير مصنف", "Uncategorised", 0m, 0, isUncategorised: true)).ToList();
        var items = books.Select((b, i) => Enumerable.Range(1, ItemsPerGradebook).Select(n => GradeItem.Manual(b, categories[i], $"تقييم {n}", $"Assessment {n}", 10m, n)).ToList()).ToList();
        foreach (var item in items.SelectMany(x => x))
        {
            item.Publish(now);
        }

        var entries = new List<GradeEntry>(count * classCourses.Count * ItemsPerGradebook);
        for (var b = 0; b < books.Count; b++)
        {
            foreach (var item in items[b])
            {
                for (var s = 0; s < students.Count; s++)
                {
                    var entry = new GradeEntry(item, students[s].Id, null, now);
                    entry.SetPoints((s * 7 + b * 3 + item.Order) % 11, null, now);
                    entries.Add(entry);
                }
            }
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await bulk.InsertAsync([cls], ct);
        await bulk.InsertAsync(classCourses, ct);
        await bulk.InsertAsync(classCourses.Select(cc => new CourseAllocation(courses.Single(c => c.Id == cc.CourseId), unit)).ToList(), ct);
        if (accounts is { } t)
        {
            await bulk.InsertAsync(classCourses.Select(cc => new ClassCourseTeacher(cc, t.Teacher, ClassCourseTeacher.PrimaryRole, year.StartDate)).ToList(), ct);
            await bulk.InsertAsync(courses.Select(course => new TeacherCourseEligibility(unit, t.Teacher, course)).ToList(), ct);
        }

        await bulk.InsertAsync(students, ct);
        await bulk.InsertAsync(guardians, ct);
        await bulk.InsertAsync(students.Select((s, i) => StudentGuardian.For(s, guardians[i], "Father", true)).ToList(), ct);
        await bulk.InsertAsync(placements, ct);
        await bulk.InsertAsync(enrollments, ct);
        await bulk.InsertAsync(books, ct);
        await bulk.InsertAsync(categories, ct);
        await bulk.InsertAsync(items.SelectMany(x => x).ToList(), ct);
        await bulk.InsertAsync(entries, ct);
        await tx.CommitAsync(ct);
    }

    private static Notification Notification(Guid user, int i, DateTime now)
    {
        var n = new Notification(OrganizationId, user, NotificationEvents.GradesPublished, "{\"CourseNameAr\":\"مادة\",\"CourseNameEn\":\"Subject\"}", "/grades", now.AddMinutes(-i));
        if (i % 2 == 0)
        {
            n.MarkRead(now);
        }

        return n;
    }

    private static string SchoolCode(int school) => $"PERF-S{school}";

    private static string SectionCode((int School, int Stage, int Section) p) => $"PERF-S{p.School}-T{p.Stage}-C{p.Section}";

    private static DevSeedData.ClientSeed TreeFor(List<(int School, int Stage, int Section)> plan, int schoolCount)
    {
        var schools = Enumerable.Range(0, schoolCount).Select(s => new DevSeedData.UnitSeed(
            SchoolCode(s), "School", $"مدرسة {s + 1}", $"School {s + 1}", false,
            plan.Where(p => p.School == s).Select(p => p.Stage).Distinct().Order().Select(stage => new DevSeedData.UnitSeed(
                $"PERF-S{s}-T{stage}", "Stage", $"مرحلة {stage + 1}", $"Stage {stage + 1}", true,
                plan.Where(p => p.School == s && p.Stage == stage).Select(p => new DevSeedData.UnitSeed(
                    SectionCode(p), "Section", $"فصل {p.Section + 1}", $"Section {p.Section + 1}")).ToArray())).ToArray())).ToArray();
        return new DevSeedData.ClientSeed(
            OrganizationId, "مجموعة اختبار الأداء", "Performance Test Group", OrganizationType.SchoolGroup, "perf",
            [new("مجموعة", "Group"), new("مدرسة", "School"), new("مرحلة", "Stage"), new("فصل", "Section", CanHostClasses: true)],
            new DevSeedData.UnitSeed("PERF", "Group", "مجموعة اختبار الأداء", "Performance Test Group", false, schools));
    }

    private sealed class NoScaleUsage : Lms.Domain.Grading.IScaleUsage
    {
        public Task<bool> IsInUseAsync(Guid scaleId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private static async Task<GradingScale> ConfirmedScaleAsync(LmsDbContext db, OrgUnit unit, CancellationToken ct)
    {
        var scales = new GradingScaleService(db, new NoScaleUsage(), new SaudiPresetCatalog());
        var existing = await db.Set<GradingScale>().FirstOrDefaultAsync(s => s.OrgUnitId == unit.Id && s.SeedKey == SaudiPresetSeed.SchoolKey && !s.IsTemplate, ct);
        var scale = existing ?? await scales.CopyPresetAsync(unit, SaudiPresetSeed.SchoolKey, ct) ?? throw new InvalidOperationException("Unknown preset.");
        if (scale.Provisional)
        {
            await scales.ConfirmAsync(scale, ct);
        }

        return scale;
    }
}
