using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Grading;
using Lms.Domain.People;
using Lms.Infrastructure.Grading.Gradebooks;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Reads;

/// <summary>
/// Staff grade reports (M5 Task 9): one row per student per course with the final and band, from the same engine as the grid. Unlike the
/// student reads, published and unpublished items both count. The caller decides which class courses the reader may see; this service never
/// widens that list. Read only.
/// </summary>
public sealed class GradeReportService(
    LmsDbContext db, GradebookCalculation calculation, GradeLookups lookups, EventTermResolver terms, IClock clock)
{
    /// <param name="classCourses">The class courses the caller may read (already filtered by relationship and unit).</param>
    /// <param name="onlyStudent">Limits the rows to one student (the student report).</param>
    public async Task<GradeReportDto> BuildAsync(
        string kind, IReadOnlyList<ClassCourse> classCourses, Guid? onlyStudent, Guid? termId, int page, int pageSize, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var rows = new List<GradeReportRowDto>();
        foreach (var classCourse in classCourses)
        {
            var term = termId ?? (await terms.ResolveAsync(classCourse, now, ct))?.Id;
            var book = term is null ? null : await db.Set<Gradebook>().FirstOrDefaultAsync(g => g.ClassCourseId == classCourse.Id && g.TermId == term, ct);
            if (book is null)
            {
                continue;
            }

            var studentIds = await db.Set<CourseEnrollment>()
                .Where(e => e.ClassCourseId == classCourse.Id && (onlyStudent == null || e.StudentId == onlyStudent))
                .Select(e => e.StudentId).Distinct().ToListAsync(ct);
            if (studentIds.Count == 0)
            {
                continue;
            }

            var students = await db.Set<StudentProfile>().Where(s => studentIds.Contains(s.Id)).ToListAsync(ct);
            var loaded = await calculation.RunAsync(book, studentIds, publishedOnly: false, ct);
            var (courseAr, courseEn) = await lookups.CourseNameAsync(classCourse, ct);
            foreach (var student in students)
            {
                var result = loaded.Result.Students.FirstOrDefault(s => s.StudentId == student.Id);
                rows.Add(new GradeReportRowDto(
                    student.Id, student.NameAr, student.NameEn, student.StudentCode, classCourse.Id, courseAr, courseEn,
                    result?.FinalPercent, GradebookCalculation.CodeOf(loaded, result?.Band), result?.Band?.LabelAr, result?.Band?.LabelEn,
                    result?.Band?.GpaPoints));
            }
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, Paging.MaxPageSize);
        var ordered = rows.OrderBy(r => r.StudentNumber, StringComparer.Ordinal).ThenBy(r => r.CourseNameEn, StringComparer.Ordinal)
            .ThenBy(r => r.ClassCourseId).ToList();
        return new GradeReportDto(
            kind, new PagedResult<GradeReportRowDto>(ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), ordered.Count, page, pageSize),
            now, now);
    }
}
