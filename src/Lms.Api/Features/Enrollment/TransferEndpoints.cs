using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Enrollment;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Enrollment;

/// <summary>
/// Transfer of a student between classes of one academic year (FR-ENR-05, D43): preview, transfer, history. A transfer across
/// schools needs both units visible to the caller; the permission is checked at both class units.
/// </summary>
public static class TransferEndpoints
{
    private const int MaxReasonLength = 500;

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.TransfersPreview, PreviewAsync).RequireAuthorization().RequirePermission(Permissions.Enrollment.Transfer);
        app.MapPost(Routes.Transfers, TransferAsync).RequireAuthorization().RequirePermission(Permissions.Enrollment.Transfer);
        app.MapGet(Routes.StudentTransfersPattern, HistoryAsync).RequireAuthorization().RequirePermission(Permissions.Enrollment.View);
    }

    /// <summary>The student, the target class and the class the student is in now; each unit checked for the permission (404 when not visible).</summary>
    private static async Task<(StudentProfile? Student, Class? To, IResult? Problem)> LoadAsync(
        TransferStudentRequest request, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var student = await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.Id == request.StudentId, ct);
        var to = await db.Set<Class>().FirstOrDefaultAsync(c => c.Id == request.ToClassId, ct);
        if (student is null || to is null)
        {
            return (null, null, ApiProblems.NotFound());
        }

        var target = await http.CheckUnitAsync(Permissions.Enrollment.Transfer, to.OrgUnitId, ct);
        if (target.Problem is not null)
        {
            return (null, null, target.Problem);
        }

        var fromClassId = await db.Set<ClassStudent>()
            .Where(p => p.StudentId == student.Id && p.Status == ClassStudent.ActiveStatus && p.AcademicYearId == to.AcademicYearId)
            .Select(p => (Guid?)p.ClassId).FirstOrDefaultAsync(ct);
        if (fromClassId is { } id)
        {
            var from = await db.Set<Class>().FirstOrDefaultAsync(c => c.Id == id, ct);
            if (from is null)
            {
                return (null, null, ApiProblems.NotFound()); // the class the student is in lies outside the caller's scope
            }

            var source = await http.CheckUnitAsync(Permissions.Enrollment.Transfer, from.OrgUnitId, ct);
            if (source.Problem is not null)
            {
                return (null, null, source.Problem);
            }
        }

        return (student, to, null);
    }

    private static async Task<IResult> PreviewAsync(
        TransferStudentRequest request, HttpContext http, LmsDbContext db, EnrollmentEngine engine, CancellationToken ct)
    {
        var (student, to, problem) = await LoadAsync(request, http, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        try
        {
            return Results.Ok(await engine.PreviewTransferAsync(student!, to!, request.EffectiveOn, ct));
        }
        catch (EnrollmentRuleException rule)
        {
            return EnrollmentProblems.From(rule);
        }
        catch (KeyNotFoundException)
        {
            return ApiProblems.NotFound();
        }
    }

    private static async Task<IResult> TransferAsync(
        TransferStudentRequest request, HttpContext http, LmsDbContext db, IEnrollmentEngine engine, TransferNotifier notifier,
        EnrollmentQueries queries, CancellationToken ct)
    {
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > MaxReasonLength)
        {
            return EnrollmentProblems.Invalid(nameof(request.Reason), "reason_invalid");
        }

        var (student, to, problem) = await LoadAsync(request, http, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        StudentTransfer transfer;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            try
            {
                transfer = await engine.TransferAsync(student!, to!, request.EffectiveOn, reason, request.ConfirmOverCapacity, ct);
                await transaction.CommitAsync(ct);
            }
            catch (EnrollmentRuleException rule)
            {
                await transaction.RollbackAsync(ct);
                return EnrollmentProblems.From(rule);
            }
            catch (KeyNotFoundException)
            {
                await transaction.RollbackAsync(ct);
                return ApiProblems.NotFound();
            }
        }

        var from = await db.Set<Class>().FirstAsync(c => c.Id == transfer.FromClassId, ct);
        await notifier.NotifyAsync(student!, from, to!, request.EffectiveOn, ct); // after the commit; never undoes it
        var dto = (await queries.TransferDtosAsync([transfer], ct)).Single();
        return Results.Created(Routes.StudentTransfers(student!.Id), dto);
    }

    private static async Task<IResult> HistoryAsync(
        Guid id, HttpContext http, LmsDbContext db, IRelationshipScope scope, EnrollmentQueries queries, CancellationToken ct)
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

        // Teaching-only for a teacher (D42): only a student they teach in one of their class courses.
        var filter = await scope.ForAsync(Permissions.Enrollment.View, ct);
        if (!filter.IsUnrestricted)
        {
            var own = filter.ClassCourseIds.ToList();
            if (!await db.Set<CourseEnrollment>().AnyAsync(e => e.StudentId == id && own.Contains(e.ClassCourseId), ct))
            {
                return ApiProblems.NotFound();
            }
        }

        var rows = await db.Set<StudentTransfer>().Where(t => t.StudentId == id).OrderByDescending(t => t.EffectiveOn).ThenByDescending(t => t.CreatedAt).ToListAsync(ct);
        return Results.Ok(await queries.TransferDtosAsync(rows, ct));
    }
}
