using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Enrollment;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Enrollment;

/// <summary>Students of a class: place (up to 500, all or nothing), list, remove (M3 Task 24).</summary>
public static class ClassStudentEndpoints
{
    public const int MaxStudentsPerRequest = 500;
    private const int MaxReasonLength = 500;

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.ClassStudentsPattern, AddAsync).RequireAuthorization().RequirePermission(Permissions.Enrollment.Manage);
        app.MapGet(Routes.ClassStudentsPattern, ListAsync).RequireAuthorization().RequirePermission(Permissions.Classes.View);
        app.MapPost(Routes.ClassStudentRemovePattern, RemoveAsync).RequireAuthorization().RequirePermission(Permissions.Enrollment.Manage);
    }

    private static async Task<IResult> AddAsync(
        Guid id, AddStudentsToClassRequest request, HttpContext http, LmsDbContext db, IEnrollmentEngine engine, IClock clock,
        CancellationToken ct)
    {
        var cls = await db.Set<Class>().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cls is null)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.Enrollment.Manage, cls.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var ids = request.StudentIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0)
        {
            return EnrollmentProblems.Invalid(nameof(request.StudentIds), "students_required");
        }

        if (ids.Count > MaxStudentsPerRequest)
        {
            return ApiProblems.BadRequest(ProblemCodes.too_many_items);
        }

        // Students outside the caller's scope do not exist for them: any missing id answers 404 for the whole request.
        var students = await db.Set<StudentProfile>().Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        if (students.Count != ids.Count)
        {
            return ApiProblems.NotFound();
        }

        try
        {
            var results = await engine.PlaceManyAsync(
                students, cls, request.EffectiveOn ?? DateOnly.FromDateTime(clock.UtcNow), request.ConfirmOverCapacity, ct);
            return Results.Ok(new AddStudentsResultDto(
                results.Count,
                results.Sum(r => r.EnrollmentsCreated),
                results.SelectMany(r => r.Skips).Select(s => new RestrictionSkipDto(s.StudentId, s.ClassCourseId, s.FailedTypes)).ToList(),
                results.Any(r => r.OverCapacity)));
        }
        catch (EnrollmentRuleException rule)
        {
            return EnrollmentProblems.From(rule);
        }
    }

    private static async Task<IResult> ListAsync(
        Guid id, HttpContext http, LmsDbContext db, IRelationshipScope scope, EnrollmentQueries queries,
        CancellationToken ct)
    {
        var cls = await db.Set<Class>().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cls is null)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.Classes.View, cls.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        // A teacher sees the students of a class through a class course they teach or as its homeroom teacher (D42).
        if (!(await scope.ForAsync(Permissions.Classes.View, ct)).AllowsClass(id))
        {
            return ApiProblems.NotFound();
        }

        return Results.Ok(await queries.ClassStudentsAsync(id, ct));
    }

    private static async Task<IResult> RemoveAsync(
        Guid id, Guid classStudentId, RemoveFromClassRequest request, HttpContext http, LmsDbContext db, IEnrollmentEngine engine,
        CancellationToken ct)
    {
        var placement = await db.Set<ClassStudent>().FirstOrDefaultAsync(c => c.Id == classStudentId && c.ClassId == id, ct);
        if (placement is null)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.Enrollment.Manage, placement.OrgUnitId, ct);
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
            await engine.RemoveFromClassAsync(placement, reason, request.EffectiveOn, ct);
            return Results.NoContent();
        }
        catch (EnrollmentRuleException rule)
        {
            return EnrollmentProblems.From(rule);
        }
    }
}
