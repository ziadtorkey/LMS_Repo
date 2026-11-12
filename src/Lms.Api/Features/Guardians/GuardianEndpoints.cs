using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Organizations;
using Lms.Domain.Access;
using Lms.Domain.People;
using Lms.Infrastructure.People.Guardians;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Guardians;

/// <summary>Guardians as people: a paged list, one guardian with the children the caller can see, edit, and the client-wide match (D30).</summary>
public static class GuardianEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.Guardians, ListAsync).RequirePermission(Permissions.Students.View);
        // Registered before the {id} route so "match" is never read as an id.
        app.MapGet(Routes.GuardiansMatch, MatchAsync).RequirePermission(Permissions.Students.Update);
        app.MapGet(Routes.GuardianPattern, GetAsync).RequirePermission(Permissions.Students.View);
        app.MapPut(Routes.GuardianPattern, UpdateAsync).RequirePermission(Permissions.Students.Update);
    }

    private static async Task<IResult> ListAsync(
        string? search, int? page, int? pageSize, LmsDbContext db, ICurrentContext context, IRelationshipScope relationships, CancellationToken ct)
    {
        var p = page ?? 1;
        var size = pageSize ?? 50;
        if (p < 1 || size < 1 || size > Paging.MaxPageSize)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { [p < 1 ? "page" : "pageSize"] = ["paging_invalid"] },
                extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });
        }

        // A platform admin without a client context acts on no client's guardians.
        var filter = await relationships.ForAsync(Permissions.Students.View, ct);
        var guardians = context.OrganizationId is null ? GuardianVisibility.Visible(db).Where(_ => false) : GuardianVisibility.Visible(db, filter);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim();
            var key = PersonNormalizer.NameKey(text);
            var mobile = PersonNormalizer.Mobile(text);
            var nationalId = PersonNormalizer.NationalId(text);
            guardians = guardians.Where(g =>
                (key != string.Empty && (g.NameArKey.Contains(key) || g.NameEnKey.Contains(key)))
                || (mobile != null && g.Mobile == mobile) || (nationalId != null && g.NationalId == nationalId));
        }

        var ordered = guardians.OrderBy(g => g.NameEnKey).ThenBy(g => g.Id);
        var total = await ordered.CountAsync(ct);
        var rows = await ordered.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        var children = await GuardianMapper.ChildrenInScopeAsync(db, rows.Select(g => g.Id).ToList(), filter, ct);
        return Results.Ok(new PagedResult<GuardianSummaryDto>(
            rows.Select(g => GuardianMapper.Summary(g, children.GetValueOrDefault(g.Id))).ToList(), total, p, size));
    }

    private static async Task<IResult> GetAsync(Guid id, LmsDbContext db, ICurrentContext context, IRelationshipScope relationships, CancellationToken ct)
    {
        if (context.OrganizationId is null)
        {
            return ApiProblems.NotFound();
        }

        var filter = await relationships.ForAsync(Permissions.Students.View, ct);
        var guardian = await GuardianVisibility.Visible(db, filter).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (guardian is null)
        {
            return ApiProblems.NotFound();
        }

        // Only the children inside the caller's scope (and, for a Teacher, the students they reach): unit-scoped tables.
        var reachable = GuardianVisibility.ReachableStudentIds(db, filter);
        var rows = await (
            from l in db.Set<StudentGuardian>()
            where l.GuardianId == id && reachable.Contains(l.StudentId)
            join s in db.Set<StudentProfile>() on l.StudentId equals s.Id
            join u in db.Set<OrgUnit>() on s.OrgUnitId equals u.Id
            orderby s.NameEnKey
            select new GuardianChildDto(s.Id, s.StudentCode, s.NameAr, s.NameEn, l.Relation, l.IsPrimary, u.NameAr, u.NameEn))
            .ToListAsync(ct);
        return Results.Ok(new GuardianDetailDto(GuardianMapper.Summary(guardian, rows.Count), guardian.NationalId, rows));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateGuardianRequest? body, HttpContext http, LmsDbContext db, ICurrentContext context, IGuardianLinker linker,
        CancellationToken ct)
    {
        if (context.OrganizationId is null)
        {
            return ApiProblems.NotFound();
        }

        if (await GuardianVisibility.Visible(db).AnyAsync(g => g.Id == id, ct) is false)
        {
            return ApiProblems.NotFound();
        }

        // Any school with a child of the guardian may edit the guardian (D30): Students.Update at one of those children's units.
        var units = await db.Set<StudentGuardian>().Where(l => l.GuardianId == id).Select(l => l.OrgUnitId).Distinct().ToListAsync(ct);
        IResult? lastRefusal = null;
        var allowed = false;
        foreach (var unit in units)
        {
            var access = await http.CheckUnitAsync(Permissions.Students.Update, unit, ct);
            if (access.Problem is null)
            {
                allowed = true;
                break;
            }

            lastRefusal = access.Problem;
        }

        if (!allowed)
        {
            return lastRefusal ?? ApiProblems.NotFound();
        }

        if (body?.Data is null)
        {
            return GuardianValidators.ToProblem(new GuardianLinkException(ProblemCodes.validation_failed, "body"));
        }

        try
        {
            var guardian = await linker.UpdateGuardianAsync(id, GuardianValidators.ToDomain(body.Data), ct);
            var children = await GuardianMapper.ChildrenInScopeAsync(db, [id], RelationshipFilter.Unrestricted, ct);
            return Results.Ok(GuardianMapper.Summary(guardian, children.GetValueOrDefault(id)));
        }
        catch (GuardianLinkException ex)
        {
            return GuardianValidators.ToProblem(ex);
        }
    }

    /// <summary>
    /// Is this person already registered in the client? Exact normalized national id (first) or mobile only, at most one result,
    /// and only the name, a masked mobile and a children count are returned: never a child, a school or a contact detail (D30).
    /// </summary>
    private static async Task<IResult> MatchAsync(
        string? nationalId, string? mobile, LmsDbContext db, ICurrentContext context, IPersonDuplicateFinder finder, CancellationToken ct)
    {
        var id = PersonNormalizer.NationalId(nationalId);
        var phone = PersonNormalizer.Mobile(mobile);
        if (id is null && phone is null)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["nationalId"] = ["required"] },
                extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });
        }

        if (context.OrganizationId is not { } organizationId)
        {
            return Results.NoContent();
        }

        var guardian = await finder.FindGuardianAsync(organizationId, id, phone, ct);
        if (guardian is null)
        {
            return Results.NoContent();
        }

        // ISOLATION-BYPASS: how many children the guardian has in the whole client, shown as a bare number so the caller can tell
        // a sibling's guardian from a stranger. Limited to this client and live links; no student or unit leaves this query.
        var children = await db.Set<StudentGuardian>().IgnoreQueryFilters()
            .Where(l => l.OrganizationId == organizationId && !l.IsDeleted && l.GuardianId == guardian.Id)
            .Select(l => l.StudentId).Distinct().CountAsync(ct);
        return Results.Ok(new GuardianMatchDto(guardian.Id, guardian.NameAr, guardian.NameEn, GuardianMapper.MaskMobile(guardian.Mobile), children));
    }
}
