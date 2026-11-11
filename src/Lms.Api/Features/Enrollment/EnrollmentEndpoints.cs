using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Enrollment;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Enrollment;

/// <summary>Course enrollments: manual (optional courses), drop, and the relationship-scoped reads (M3 Task 24).</summary>
public static class EnrollmentEndpoints
{
    private const int MaxReasonLength = 500;

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.ClassCourseEnrollmentsPattern, EnrollAsync).RequireAuthorization().RequirePermission(Permissions.Enrollment.Manage);
        app.MapGet(Routes.ClassCourseEnrollmentsPattern, ListOfClassCourseAsync).RequireAuthorization().RequirePermission(Permissions.Enrollment.View);
        app.MapPost(Routes.EnrollmentDropPattern, DropAsync).RequireAuthorization().RequirePermission(Permissions.Enrollment.Manage);
        app.MapGet(Routes.StudentEnrollmentsPattern, ListOfStudentAsync).RequireAuthorization().RequirePermission(Permissions.Enrollment.View);
        app.MapGet(Routes.Enrollments, QueryAsync).RequireAuthorization().RequirePermission(Permissions.Enrollment.View);
    }

    private static async Task<IResult> EnrollAsync(
        Guid id, EnrollStudentsRequest request, HttpContext http, LmsDbContext db, IEnrollmentEngine engine, EnrollmentQueries queries,
        CancellationToken ct)
    {
        var classCourse = await db.Set<ClassCourse>().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (classCourse is null)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.Enrollment.Manage, classCourse.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var ids = request.StudentIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0)
        {
            return EnrollmentProblems.Invalid(nameof(request.StudentIds), "students_required");
        }

        if (ids.Count > ClassStudentEndpoints.MaxStudentsPerRequest)
        {
            return ApiProblems.BadRequest(ProblemCodes.too_many_items);
        }

        var students = await db.Set<StudentProfile>().Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        if (students.Count != ids.Count)
        {
            return ApiProblems.NotFound();
        }

        // All or nothing: one transaction around the whole list (each enrollment is checked and saved by the engine).
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var created = new List<CourseEnrollment>();
            foreach (var student in students)
            {
                created.Add(await engine.EnrollManualAsync(classCourse, student, EnrollmentSources.Manual, ct));
            }

            await transaction.CommitAsync(ct);
            return Results.Ok(await queries.ToDtosAsync(created, ct));
        }
        catch (EnrollmentRuleException rule)
        {
            await transaction.RollbackAsync(ct);
            return EnrollmentProblems.From(rule);
        }
    }

    private static async Task<IResult> ListOfClassCourseAsync(
        Guid id, HttpContext http, LmsDbContext db, EnrollmentRelationship relationship, EnrollmentQueries queries,
        CancellationToken ct, string? status = EnrollmentStatuses.Active, int page = 1, int pageSize = Paging.DefaultPageSize)
    {
        var classCourse = await db.Set<ClassCourse>().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (classCourse is null)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.Enrollment.View, classCourse.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        if (await relationship.RestrictsAsync(Permissions.Enrollment.View, ct)
            && !await relationship.TeachingClassCourses().AnyAsync(c => c.Id == id, ct))
        {
            return ApiProblems.NotFound(); // a teacher cannot tell another teacher's class course from a missing one
        }

        return await PageAsync(db.Set<CourseEnrollment>().Where(e => e.ClassCourseId == id), status, page, pageSize, queries, ct);
    }

    private static async Task<IResult> DropAsync(
        Guid id, DropEnrollmentRequest request, HttpContext http, LmsDbContext db, IEnrollmentEngine engine, IClock clock,
        CancellationToken ct)
    {
        var enrollment = await db.Set<CourseEnrollment>().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (enrollment is null)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.Enrollment.Manage, enrollment.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > MaxReasonLength)
        {
            return EnrollmentProblems.Invalid(nameof(request.Reason), "reason_invalid");
        }

        try
        {
            await engine.DropAsync(enrollment, reason, request.EffectiveOn ?? DateOnly.FromDateTime(clock.UtcNow), ct);
            return Results.NoContent();
        }
        catch (EnrollmentRuleException rule)
        {
            return EnrollmentProblems.From(rule);
        }
    }

    private static async Task<IResult> ListOfStudentAsync(
        Guid id, HttpContext http, LmsDbContext db, EnrollmentRelationship relationship, EnrollmentQueries queries,
        CancellationToken ct, string? status = null, int page = 1, int pageSize = Paging.DefaultPageSize)
    {
        var student = await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (student is null)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.Enrollment.View, student.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var enrollments = db.Set<CourseEnrollment>().Where(e => e.StudentId == id);
        if (await relationship.RestrictsAsync(Permissions.Enrollment.View, ct))
        {
            // A teacher sees a student's enrollments only in their own class courses; a student outside them is not found.
            var own = relationship.TeachingClassCourses().Select(c => c.Id);
            enrollments = enrollments.Where(e => own.Contains(e.ClassCourseId));
            if (!await enrollments.AnyAsync(ct))
            {
                return ApiProblems.NotFound();
            }
        }

        return await PageAsync(enrollments, status, page, pageSize, queries, ct);
    }

    private static async Task<IResult> QueryAsync(
        [AsParameters] EnrollmentQuery query, LmsDbContext db, EnrollmentRelationship relationship, EnrollmentQueries queries,
        CancellationToken ct)
    {
        IQueryable<CourseEnrollment> enrollments = db.Set<CourseEnrollment>();
        if (query.StudentId is { } studentId)
        {
            enrollments = enrollments.Where(e => e.StudentId == studentId);
        }

        if (query.ClassCourseId is { } classCourseId)
        {
            enrollments = enrollments.Where(e => e.ClassCourseId == classCourseId);
        }

        if (await relationship.RestrictsAsync(Permissions.Enrollment.View, ct))
        {
            var own = relationship.TeachingClassCourses().Select(c => c.Id);
            enrollments = enrollments.Where(e => own.Contains(e.ClassCourseId));
        }

        return await PageAsync(enrollments, query.Status, query.Page, query.PageSize, queries, ct);
    }

    private static async Task<IResult> PageAsync(
        IQueryable<CourseEnrollment> enrollments, string? status, int page, int pageSize, EnrollmentQueries queries,
        CancellationToken ct)
    {
        if (page < 1 || pageSize < 1 || pageSize > Paging.MaxPageSize)
        {
            return EnrollmentProblems.Invalid(page < 1 ? nameof(page) : nameof(pageSize), "paging_invalid");
        }

        if (status is not null)
        {
            if (!EnrollmentStatuses.All.Contains(status))
            {
                return EnrollmentProblems.Invalid(nameof(status), "status_invalid");
            }

            enrollments = enrollments.Where(e => e.Status == status);
        }

        var total = await enrollments.CountAsync(ct);
        var rows = await enrollments.OrderBy(e => e.From).ThenBy(e => e.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Results.Ok(new PagedResult<EnrollmentDto>(await queries.ToDtosAsync(rows, ct), total, page, pageSize));
    }
}
