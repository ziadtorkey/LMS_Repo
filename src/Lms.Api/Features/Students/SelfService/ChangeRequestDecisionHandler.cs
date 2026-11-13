using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Students.SelfService;

/// <summary>
/// The approval queue (D27): Org Admin, Manager and Counselor decide a request of a student in their unit. A request outside the
/// caller's unit scope is 404; a closed one is 409; one whose fields changed meanwhile is 409 stale.
/// </summary>
internal sealed class ChangeRequestDecisionHandler(
    LmsDbContext db, ICurrentContext context, IClock clock, StudentChangeApplier applier, ChangeRequestMapper mapper,
    ChangeRequestNotifier notifier)
{
    private const int MaxComment = 1000;

    public async Task<IResult> ListAsync(ChangeRequestQuery query, HttpContext http, CancellationToken ct)
    {
        if (query.Page < 1 || query.PageSize < 1 || query.PageSize > Paging.MaxPageSize)
        {
            return StudentValidators.Problem(new Dictionary<string, string[]> { [query.Page < 1 ? "Page" : "PageSize"] = ["paging_invalid"] });
        }

        if (query.Status is not null && !ChangeRequestStatuses.All.Contains(query.Status))
        {
            return StudentValidators.Problem(new Dictionary<string, string[]> { ["Status"] = ["invalid_value"] });
        }

        var scope = await ScopeUnitAsync(query.OrgUnitId, http, ct);
        if (scope.Problem is not null)
        {
            return scope.Problem;
        }

        var path = scope.Unit!.Path;
        var requests = db.Set<StudentChangeRequest>().Where(r => r.OrgUnitPath.StartsWith(path));
        if (query.Status is not null)
        {
            var status = Enum.Parse<ChangeRequestStatus>(query.Status);
            requests = requests.Where(r => r.Status == status);
        }

        var ordered = requests.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id);
        var total = await ordered.CountAsync(ct);
        var rows = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Results.Ok(new PagedResult<StudentChangeRequestDto>(await mapper.ToDtosAsync(rows, ct), total, query.Page, query.PageSize));
    }

    public async Task<IResult> GetAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var (request, problem) = await FindAsync(id, http, ct);
        return problem ?? Results.Ok((await mapper.ToDtosAsync([request!], ct))[0]);
    }

    public async Task<IResult> ApproveAsync(Guid id, DecideChangeRequestRequest? body, HttpContext http, CancellationToken ct)
    {
        var comment = Comment(body);
        if (comment is { Length: > MaxComment })
        {
            return StudentValidators.Problem(new Dictionary<string, string[]> { ["Comment"] = ["too_long"] });
        }

        var (request, problem) = await FindAsync(id, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (!request!.IsPending)
        {
            return ApiProblems.Conflict(ProblemCodes.change_request_closed);
        }

        var student = await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.Id == request.StudentId, ct);
        if (student is null || context.UserId is not { } userId)
        {
            return ApiProblems.NotFound();
        }

        var changes = ChangeRequestMapper.ChangesOf(request);
        if (changes.Any(c => !StudentFieldValues.Same(StudentFieldValues.Read(student, c.Field), c.Before)))
        {
            return ApiProblems.Conflict(ProblemCodes.change_request_stale);
        }

        var (data, invalid) = await applier.PrepareAsync(student, changes.ToDictionary(c => c.Field, c => c.After), ct);
        if (invalid is not null)
        {
            return invalid;
        }

        StudentChangeApplier.Apply(student, data!);
        request.Approve(userId, clock.UtcNow, comment);
        await db.SaveChangesAsync(ct);
        await notifier.DecidedAsync(student, request, ct);
        return Results.Ok((await mapper.ToDtosAsync([request], ct))[0]);
    }

    public async Task<IResult> RejectAsync(Guid id, DecideChangeRequestRequest? body, HttpContext http, CancellationToken ct)
    {
        var comment = Comment(body);
        if (comment is null || comment.Length > MaxComment)
        {
            return StudentValidators.Problem(new Dictionary<string, string[]> { ["Comment"] = [comment is null ? "required" : "too_long"] });
        }

        var (request, problem) = await FindAsync(id, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (!request!.IsPending)
        {
            return ApiProblems.Conflict(ProblemCodes.change_request_closed);
        }

        var student = await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.Id == request.StudentId, ct);
        if (student is null || context.UserId is not { } userId)
        {
            return ApiProblems.NotFound();
        }

        request.Reject(userId, clock.UtcNow, comment);
        await db.SaveChangesAsync(ct);
        await notifier.DecidedAsync(student, request, ct);
        return Results.Ok((await mapper.ToDtosAsync([request], ct))[0]);
    }

    private static string? Comment(DecideChangeRequestRequest? body) => string.IsNullOrWhiteSpace(body?.Comment) ? null : body.Comment.Trim();

    private async Task<(StudentChangeRequest? Request, IResult? Problem)> FindAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var request = context.OrganizationId is null ? null : await db.Set<StudentChangeRequest>().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
        {
            return (null, ApiProblems.NotFound());
        }

        var access = await http.CheckUnitAsync(Permissions.Students.ApproveChanges, request.OrgUnitId, ct);
        return access.Problem is not null ? (null, access.Problem) : (request, null);
    }

    private async Task<UnitAccessResult> ScopeUnitAsync(Guid? orgUnitId, HttpContext http, CancellationToken ct)
    {
        if (orgUnitId is { } given)
        {
            return await http.CheckUnitAsync(Permissions.Students.ApproveChanges, given, ct);
        }

        var scopePath = context.ScopePath;
        var unit = scopePath is null ? null : await db.Set<OrgUnit>().FirstOrDefaultAsync(u => u.Path == scopePath, ct);
        return unit is null
            ? new UnitAccessResult(null, ApiProblems.NotFound())
            : await http.CheckUnitAsync(Permissions.Students.ApproveChanges, unit.Id, ct);
    }
}
