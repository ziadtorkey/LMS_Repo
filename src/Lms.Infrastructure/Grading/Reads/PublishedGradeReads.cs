using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Grading;
using Lms.Domain.Grading.Calculation;
using Lms.Domain.People;
using Lms.Infrastructure.Grading.Gradebooks;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Reads;

/// <summary>
/// The only way a student, a guardian or the staff "what the student sees" preview reads grades (M5 L6). Only published items are ever loaded,
/// totals and bands come from the engine over those items, an unpublished item contributes no title, no count, no weight and no marker, and the
/// read never writes. A course without a gradebook is simply absent.
/// </summary>
public sealed class PublishedGradeReads(
    LmsDbContext db, GradebookCalculation calculation, GradeLookups lookups, EventTermResolver terms, IClock clock)
{
    /// <summary>All courses of the student in the term (null = the term current for each course).</summary>
    public async Task<StudentGradesDto> ForStudentAsync(StudentProfile student, Guid? termId, CancellationToken ct)
    {
        var classCourseIds = await db.Set<CourseEnrollment>().Where(e => e.StudentId == student.Id)
            .Select(e => e.ClassCourseId).Distinct().ToListAsync(ct);
        var classCourses = await db.Set<ClassCourse>().Where(c => classCourseIds.Contains(c.Id)).ToListAsync(ct);

        var courses = new List<StudentCourseGradesDto>();
        foreach (var classCourse in classCourses)
        {
            if (await CourseAsync(student, classCourse, termId, ct) is { } course)
            {
                courses.Add(course);
            }
        }

        var now = clock.UtcNow;
        return new StudentGradesDto(
            student.Id, student.NameAr, student.NameEn,
            courses.OrderBy(c => c.CourseNameEn, StringComparer.Ordinal).ThenBy(c => c.ClassCourseId).ToList(), now, now);
    }

    /// <summary>One course of the student (also the staff preview); null when there is no gradebook for the term.</summary>
    public async Task<StudentCourseGradesDto?> CourseAsync(StudentProfile student, ClassCourse classCourse, Guid? termId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var resolvedTermId = termId ?? (await terms.ResolveAsync(classCourse, now, ct))?.Id;
        if (resolvedTermId is not { } term)
        {
            return null;
        }

        var book = await db.Set<Gradebook>().FirstOrDefaultAsync(g => g.ClassCourseId == classCourse.Id && g.TermId == term, ct);
        var termRow = book is null ? null : await lookups.TermOfClassCourseAsync(classCourse, term, ct);
        if (book is null || termRow is null)
        {
            return null;
        }

        var loaded = await calculation.RunAsync(book, [student.Id], publishedOnly: true, ct);
        var result = loaded.Result.Students.FirstOrDefault(s => s.StudentId == student.Id);
        var (courseAr, courseEn) = await lookups.CourseNameAsync(classCourse, ct);
        var uncategorised = loaded.Categories.Where(c => c.IsUncategorised).Select(c => c.Id).ToHashSet();

        var items = loaded.Items.Select(item =>
        {
            var r = result?.Items.FirstOrDefault(x => x.ItemId == item.Id);
            return new PublishedGradeItemDto(
                item.Id, item.TitleAr, item.TitleEn, uncategorised.Contains(item.CategoryId) ? null : item.CategoryId,
                r?.Points, item.MaxPoints, r?.Source == ItemSource.Excused, item.PublishedAtUtc ?? DateTime.MinValue);
        }).ToList();

        var categories = loaded.Categories.Where(c => !c.IsUncategorised).Select(c => new StudentGradeCategoryDto(
            c.Id, c.NameAr, c.NameEn, c.Weight, result?.Categories.FirstOrDefault(x => x.CategoryId == c.Id)?.Percent)).ToList();

        return new StudentCourseGradesDto(
            classCourse.Id, book.Id, courseAr, courseEn, term, termRow.NameAr, termRow.NameEn, book.DecimalPlaces, items, categories,
            result?.FinalPercent, GradebookCalculation.CodeOf(loaded, result?.Band), result?.Band?.LabelAr, result?.Band?.LabelEn,
            result?.Band?.GpaPoints, BandLegendReader.Legend(loaded.Scale));
    }
}
