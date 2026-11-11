using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Enrollment;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Enrollment.Exceptions;

/// <summary>
/// The decision side of exceptional enrollment (D40): the course's supervisors decide; a course without supervisors is decided by
/// the Managers and Org Admins whose scope reaches the class. Anyone else, and anything outside the caller's unit scope, is 404.
/// Approving enrolls the student through <see cref="IEnrollmentEngine.EnrollManualAsync"/> with the source <c>Exceptional</c>.
/// </summary>
internal sealed class ExceptionDecisionHandler(
    LmsDbContext db, ICurrentContext context, IClock clock, IRelationshipScope relationships, ExceptionLookups lookups,
    IEnrollmentEngine engine, ExceptionMapper mapper, ExceptionNotifier notifier)
{
    private const int MaxComment = 1000;

    public async Task<IResult> ListAsync(ExceptionQuery query, HttpContext http, CancellationToken ct)
    {
        if (query.Page < 1 || query.PageSize < 1 || query.PageSize > Paging.MaxPageSize)
        {
            return EnrollmentProblems.Invalid(query.Page < 1 ? "Page" : "PageSize", "paging_invalid");
        }

        if (query.Status is not null && !ExceptionStatuses.All.Contains(query.Status))
        {
            return EnrollmentProblems.Invalid("Status", "invalid_value");
        }

        var scope = await ScopeUnitAsync(query.OrgUnitId, http, ct);
        if (scope.Problem is not null || context.OrganizationId is not { } organizationId || context.UserId is not { } userId)
        {
            return scope.Problem ?? ApiProblems.NotFound();
        }

        var path = scope.Unit!.Path;
        var rows =
            from e in db.Set<EnrollmentException>()
            where e.OrgUnitPath.StartsWith(path)
            join cc in db.Set<ClassCourse>() on e.ClassCourseId equals cc.Id
            select new { Exception = e, cc.CourseId };
        if (query.Status is not null)
        {
            rows = rows.Where(x => x.Exception.Status == query.Status);
        }

        var filter = await relationships.ForAsync(Permissions.Enrollment.ApproveException, ct);
        if (filter.IsUnrestricted)
        {
            // Managers and Org Admins see the requests of courses nobody supervises, and of the courses they supervise themselves.
            var supervised = await lookups.SupervisedCourseIdsAsync(organizationId, ct);
            var mine = await lookups.SupervisedByAsync(organizationId, userId, ct);
            rows = rows.Where(x => !supervised.Contains(x.CourseId) || mine.Contains(x.CourseId));
        }
        else
        {
            var courses = filter.CourseIds;
            rows = rows.Where(x => courses.Contains(x.CourseId));
        }

        var total = await rows.CountAsync(ct);
        var page = await rows.OrderByDescending(x => x.Exception.CreatedAt).ThenBy(x => x.Exception.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(x => x.Exception).ToListAsync(ct);
        return Results.Ok(new PagedResult<EnrollmentExceptionDto>(await mapper.ToDtosAsync(page, ct), total, query.Page, query.PageSize));
    }

    public async Task<IResult> GetAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var (request, _, problem) = await FindForDecisionAsync(id, http, ct);
        return problem ?? Results.Ok((await mapper.ToDtosAsync([request!], ct))[0]);
    }

    public async Task<IResult> ApproveAsync(Guid id, DecideExceptionRequest? body, HttpContext http, CancellationToken ct)
    {
        var comment = Comment(body);
        if (comment is { Length: > MaxComment })
        {
            return EnrollmentProblems.Invalid("Comment", "too_long");
        }

        var (request, cc, problem) = await FindForDecisionAsync(id, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (!request!.IsPending)
        {
            return ApiProblems.Conflict(ProblemCodes.exception_closed);
        }

        var student = await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.Id == request.StudentId, ct);
        if (student is null || context.UserId is not { } userId)
        {
            return ApiProblems.NotFound();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await engine.EnrollManualAsync(cc!, student, EnrollmentSources.Exceptional, ct);
            request.Approve(userId, clock.UtcNow, comment);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (EnrollmentRuleException rule)
        {
            await transaction.RollbackAsync(ct); // the request stays pending
            return EnrollmentProblems.From(rule);
        }

        await NotifyDecidedAsync(request, student, cc!, ct);
        return Results.Ok((await mapper.ToDtosAsync([request], ct))[0]);
    }

    public async Task<IResult> RejectAsync(Guid id, DecideExceptionRequest? body, HttpContext http, CancellationToken ct)
    {
        var comment = Comment(body);
        if (comment is null || comment.Length > MaxComment)
        {
            return EnrollmentProblems.Invalid("Comment", comment is null ? "required" : "too_long");
        }

        var (request, cc, problem) = await FindForDecisionAsync(id, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (!request!.IsPending)
        {
            return ApiProblems.Conflict(ProblemCodes.exception_closed);
        }

        var student = await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.Id == request.StudentId, ct);
        if (student is null || context.UserId is not { } userId)
        {
            return ApiProblems.NotFound();
        }

        request.Reject(userId, clock.UtcNow, comment);
        await db.SaveChangesAsync(ct);
        await NotifyDecidedAsync(request, student, cc!, ct);
        return Results.Ok((await mapper.ToDtosAsync([request], ct))[0]);
    }

    private async Task NotifyDecidedAsync(EnrollmentException request, StudentProfile student, ClassCourse cc, CancellationToken ct)
    {
        var course = (await lookups.CoursesAsync(request.OrganizationId, [cc.CourseId], ct)).GetValueOrDefault(cc.CourseId);
        if (course is not null)
        {
            await notifier.DecidedAsync(request, student, course, ct);
        }
    }

    private static string? Comment(DecideExceptionRequest? body) => string.IsNullOrWhiteSpace(body?.Comment) ? null : body.Comment.Trim();

    /// <summary>The request, when the caller may decide it: permission at the class unit, then supervisor-or-manager (D40).</summary>
    private async Task<(EnrollmentException? Request, ClassCourse? ClassCourse, IResult? Problem)> FindForDecisionAsync(
        Guid id, HttpContext http, CancellationToken ct)
    {
        var request = context.OrganizationId is null ? null : await db.Set<EnrollmentException>().FirstOrDefaultAsync(e => e.Id == id, ct);
        var cc = request is null ? null : await db.Set<ClassCourse>().FirstOrDefaultAsync(c => c.Id == request.ClassCourseId, ct);
        if (request is null || cc is null || context.UserId is not { } userId)
        {
            return (null, null, ApiProblems.NotFound());
        }

        var access = await http.CheckUnitAsync(Permissions.Enrollment.ApproveException, request.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return (null, null, access.Problem);
        }

        var supervisors = await lookups.SupervisorUserIdsAsync(request.OrganizationId, cc.CourseId, ct);
        var unrestricted = (await relationships.ForAsync(Permissions.Enrollment.ApproveException, ct)).IsUnrestricted;
        var may = supervisors.Contains(userId) || (supervisors.Count == 0 && unrestricted);
        return may ? (request, cc, null) : (null, null, ApiProblems.NotFound());
    }

    private async Task<UnitAccessResult> ScopeUnitAsync(Guid? orgUnitId, HttpContext http, CancellationToken ct)
    {
        if (orgUnitId is { } given)
        {
            return await http.CheckUnitAsync(Permissions.Enrollment.ApproveException, given, ct);
        }

        var scopePath = context.ScopePath;
        var unit = scopePath is null ? null : await db.Set<OrgUnit>().FirstOrDefaultAsync(u => u.Path == scopePath, ct);
        return unit is null
            ? new UnitAccessResult(null, ApiProblems.NotFound())
            : await http.CheckUnitAsync(Permissions.Enrollment.ApproveException, unit.Id, ct);
    }
}
