using Lms.Domain.AcademicYears;
using Lms.Domain.Classes;
using Lms.Domain.Grading;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Published-only grade reads and grade reports (M5 Task 9). The guardian route is for a signed-in guardian, so every staff caller is 403;
/// the preview and the reports are checked at the class unit (School1): School 2 and the other client get 404, a teacher without an assignment
/// to the class course gets 404 on the preview and 403 on the reports (no <c>Grades.ViewReports</c>).
/// </summary>
public sealed class GradeReadsProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = ProbeMilestones.M5 });

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("guardian child grades", HttpMethod.Get, Routes.StudentGradesPattern,
            async (w, _) => new ProbeRequest(Routes.StudentGradesPattern.Replace("{id:guid}", (await NewAsync(w)).StudentId.ToString())),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("student view preview", HttpMethod.Get, Routes.ClassCourseGradebookStudentViewPattern,
            async (w, _) =>
            {
                var rows = await NewAsync(w);
                return new ProbeRequest(Routes.ClassCourseGradebookStudentView(rows.ClassCourseId, rows.StudentId));
            },
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("grade report", HttpMethod.Get, Routes.GradeReportPattern,
            async (w, _) => new ProbeRequest(Routes.GradeReport("course") + "?classCourseId=" + (await NewAsync(w)).ClassCourseId),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
    ];

    private sealed record Made(Guid ClassCourseId, Guid StudentId);

    /// <summary>An enrolled student in a class course at School1 whose current term has a gradebook with one published item.</summary>
    private static async Task<Made> NewAsync(M1ProbeWorld w)
    {
        var factory = w.Callers.Factory;
        var rows = await AcademicTestKit.AddAllAsync(factory, w.Callers.A.School1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var cc = await db.Set<ClassCourse>().SingleAsync(c => c.Id == rows.ClassCourse.Id);
        var year = await db.Set<AcademicYear>().SingleAsync(y => y.Id == rows.Year.Id);
        var term = new Term(year, "الفصل", "Term", today.AddDays(-30), today.AddDays(60));
        db.Add(term);
        await db.SaveChangesAsync();
        var book = new Gradebook(cc, term.Id, null, 2, "PointsWeighted", "Normalise");
        var category = new GradeCategory(book, "اختبارات", "Tests", 100m, 1);
        var item = GradeItem.Manual(book, category, "اختبار", "Quiz", 10m, 1);
        item.Publish(DateTime.UtcNow);
        db.AddRange(book, category, item);
        await db.SaveChangesAsync();
        return new Made(cc.Id, rows.Student.Id);
    }
}
