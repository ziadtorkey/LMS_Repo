using Lms.Domain.AcademicYears;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Grading;
using Lms.Domain.People;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Grading;

/// <summary>
/// A class course with one student, a gradebook for the current term holding one published item, one hidden item and one scheduled item (the last
/// two titled "SECRET-…" so a leak is a string search), the student's entries on all three, a guardian of the student and a guardian of someone else.
/// </summary>
internal sealed record GradeWorld(
    ContentWorld W, Gradebook Book, Term Term, GradeItem Published, GradeItem Hidden, GradeItem Scheduled, GradeCategory Tests,
    GradeCategory SecretCategory, RoleAssignment Guardian, RoleAssignment StrangerGuardian, GradingScale Scale);

internal static class GradeReadsTestKit
{
    public const string SecretMarker = "SECRET";

    public static async Task<GradeWorld> NewAsync(LmsApiFactory factory, ContentTestHost host, string name)
    {
        var w = await host.SetUpAsync(name);
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var cc = await db.Set<ClassCourse>().SingleAsync(c => c.Id == w.ClassCourse.Id);
        var year = await db.Set<AcademicYear>().SingleAsync(y => y.Id == w.Class.AcademicYearId);
        var term = new Term(year, "الفصل", "Term " + name, today.AddDays(-30), today.AddDays(60));
        var unit = await db.OrgUnits.SingleAsync(u => u.Id == w.Tree.Root.Id);
        var scale = GradingScale.CreatePlain(unit, "مقياس", "Scale " + name, "Letter", isDefault: false);
        db.AddRange(term, scale);
        await db.SaveChangesAsync();
        db.AddRange(
            new GradingScaleBand(scale, 1, 0, 50, "F", "راسب", "Fail", "د1", "d1", 0m),
            new GradingScaleBand(scale, 2, 50, 100, "P", "ناجح", "Pass", "د2", "d2", 4m));

        var book = new Gradebook(cc, term.Id, scale.Id, 2, "PointsWeighted", "Normalise");
        var plain = new GradeCategory(book, "غير مصنف", "Uncategorised", 0m, 0, isUncategorised: true);
        var tests = new GradeCategory(book, "اختبارات", "Tests", 100m, 1);
        var secret = new GradeCategory(book, "فئة " + SecretMarker, SecretMarker + "-CAT", 0m, 2);
        var published = GradeItem.Manual(book, tests, "اختبار", "Quiz one", 10m, 1);
        published.Publish(now);
        var hidden = GradeItem.Manual(book, tests, "سري", SecretMarker + "-HIDDEN", 20m, 2);
        var scheduled = GradeItem.Manual(book, secret, "مجدول", SecretMarker + "-SCHEDULED", 10m, 3);
        scheduled.Schedule(now.AddDays(1));
        db.AddRange(book, plain, tests, secret, published, hidden, scheduled);
        foreach (var (item, points) in new[] { (published, 8m), (hidden, 2m), (scheduled, 1m) })
        {
            var entry = new GradeEntry(item, w.StudentProfileId, null, now);
            entry.SetPoints(points, null, now);
            db.Add(entry);
        }

        await db.SaveChangesAsync();

        var guardian = await GuardianAsync(factory, w, w.StudentProfileId);
        var other = await PeopleTestKit.AddStudentAsync(factory, w.Tree.School1, PeopleTestKit.NewCode());
        var stranger = await GuardianAsync(factory, w, other.Id);
        return new GradeWorld(w, book, term, published, hidden, scheduled, tests, secret, guardian, stranger, scale);
    }

    private static async Task<RoleAssignment> GuardianAsync(LmsApiFactory factory, ContentWorld w, Guid studentId)
    {
        var user = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var profile = await PeopleTestKit.AddGuardianAsync(factory, w.Tree.Org.Id, PeopleTestKit.NewMobile(), null, user);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var student = await db.Set<StudentProfile>().SingleAsync(s => s.Id == studentId);
            db.Add(StudentGuardian.For(student, await db.Set<GuardianProfile>().SingleAsync(g => g.Id == profile.Id), "Father", true));
            await db.SaveChangesAsync();
        }

        return await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Guardian, user);
    }
}
