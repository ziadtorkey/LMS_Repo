using Lms.Api.Infrastructure;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Infrastructure.Grading.Reads;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.GradeReads;

/// <summary>
/// Grade reports per student, class or course (<c>Grades.ViewReports</c>: a Supervisor reads their courses, a Manager their unit scope; a teacher
/// and a counselor are 403). The caller's relationship filter narrows the class courses before anything is computed, and a resource outside it
/// is 404.
/// </summary>
public static class GradeReportEndpoints
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet(Routes.GradeReportPattern, ReportAsync).RequirePermission(Permissions.Grades.ViewReports);

    private static async Task<IResult> ReportAsync(
        string kind, [AsParameters] GradeReportQuery query, HttpContext http, LmsDbContext db, IRelationshipScope scope,
        GradeReportService reports, CancellationToken ct)
    {
        const string permission = Permissions.Grades.ViewReports;
        if (!GradeReportKinds.All.Contains(kind))
        {
            return ApiProblems.NotFound();
        }

        List<ClassCourse> classCourses;
        Guid? onlyStudent = null;
        switch (kind)
        {
            case GradeReportKinds.Course when query.ClassCourseId is { } ccId:
                var (cc, ccProblem) = await http.CheckClassCourseAsync(permission, ccId, ct);
                if (ccProblem is not null)
                {
                    return ccProblem;
                }

                classCourses = [cc!];
                break;
            case GradeReportKinds.Class when query.ClassId is { } classId:
                var (cls, clsProblem) = await http.CheckClassAsync(permission, classId, ct);
                if (clsProblem is not null)
                {
                    return clsProblem;
                }

                var filter = await scope.ForAsync(permission, ct);
                classCourses = (await db.Set<ClassCourse>().Where(c => c.ClassId == cls!.Id).ToListAsync(ct))
                    .Where(c => filter.AllowsClassCourse(c.Id)).ToList();
                break;
            case GradeReportKinds.Student when query.StudentId is { } studentId:
                var (student, stProblem) = await http.CheckStudentAsync(permission, studentId, ct);
                if (stProblem is not null)
                {
                    return stProblem;
                }

                var stFilter = await scope.ForAsync(permission, ct);
                var ids = await db.Set<CourseEnrollment>().Where(e => e.StudentId == student!.Id).Select(e => e.ClassCourseId).Distinct().ToListAsync(ct);
                classCourses = (await db.Set<ClassCourse>().Where(c => ids.Contains(c.Id)).ToListAsync(ct))
                    .Where(c => stFilter.AllowsClassCourse(c.Id)).ToList();
                onlyStudent = student!.Id;
                break;
            default:
                return ApiProblems.BadRequest(ProblemCodes.validation_failed, kind);
        }

        return Results.Ok(await reports.BuildAsync(kind, classCourses, onlyStudent, query.TermId, query.Page, query.PageSize, ct));
    }
}
