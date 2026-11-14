using Lms.Api.Infrastructure;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Infrastructure.Grading.Reads;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.GradeReads;

/// <summary>
/// The staff preview of what one student sees in a course (M5 L6): <c>Grades.Manage</c> plus the relationship to the class course, answered by
/// the same <see cref="PublishedGradeReads"/> helper as the student route. A student is 403 (no <c>Grades.Manage</c>); a student who is not
/// enrolled in the course, or a course without a gradebook, is 404.
/// </summary>
public static class StudentViewPreviewEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet(Routes.ClassCourseGradebookStudentViewPattern, PreviewAsync).RequirePermission(Permissions.Grades.Manage);

    private static async Task<IResult> PreviewAsync(
        Guid id, Guid sid, Guid? termId, HttpContext http, LmsDbContext db, PublishedGradeReads reads, CancellationToken ct)
    {
        var (classCourse, problem) = await http.CheckClassCourseAsync(Permissions.Grades.Manage, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        var enrolled = await db.Set<CourseEnrollment>().AnyAsync(e => e.ClassCourseId == classCourse!.Id && e.StudentId == sid, ct);
        var student = enrolled ? await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.Id == sid, ct) : null;
        if (student is null)
        {
            return ApiProblems.NotFound();
        }

        var course = await reads.CourseAsync(student, classCourse!, termId, ct);
        return course is null ? ApiProblems.NotFound() : Results.Ok(course);
    }
}
