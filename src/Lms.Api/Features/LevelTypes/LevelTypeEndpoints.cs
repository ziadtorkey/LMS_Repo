using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.LevelTypes;

public static class LevelTypeEndpoints
{
    public static IEndpointRouteBuilder MapLevelTypeEndpoints(this IEndpointRouteBuilder app)
    {
        // Reading is open to anyone in the client who holds LevelTypes.View; writing is client-wide, so every write
        // also needs its permission at the client root (a School admin gets 403 OutOfScope).
        app.MapGet(Routes.LevelTypes, ListAsync).RequirePermission(Permissions.LevelTypes.View);
        app.MapPost(Routes.LevelTypes, CreateAsync).RequirePermission(Permissions.LevelTypes.Create);
        app.MapPut(Routes.LevelTypesOrder, ReorderAsync).RequirePermission(Permissions.LevelTypes.Update);
        app.MapPut(Routes.LevelTypePattern, UpdateAsync).RequirePermission(Permissions.LevelTypes.Update);
        app.MapDelete(Routes.LevelTypePattern, DeleteAsync).RequirePermission(Permissions.LevelTypes.Delete);
        return app;
    }

    private static async Task<IResult> ListAsync(HttpContext http, LmsDbContext db, ICurrentContext context, CancellationToken ct)
    {
        if (RequireClient(http, context) is { } refused)
        {
            return refused;
        }

        var types = await db.LevelTypes.OrderBy(x => x.Order).ToListAsync(ct);
        var counts = await UnitCountsAsync(db, context.OrganizationId!.Value, ct);
        return Results.Ok(types.Select(x => ToDto(x, counts.GetValueOrDefault(x.Id))).ToList());
    }

    private static async Task<IResult> CreateAsync(
        HttpContext http, LmsDbContext db, ICurrentContext context, SaveLevelTypeRequest? body, CancellationToken ct)
    {
        if (await http.CheckClientRootAsync(Permissions.LevelTypes.Create, ct) is { } refused)
        {
            return refused;
        }

        var (request, problem) = LevelTypeValidators.Validate(body);
        if (problem is not null)
        {
            return problem;
        }

        if (await NameTakenAsync(db, request!, excludeId: null, ct) is { } taken)
        {
            return taken;
        }

        var max = await db.LevelTypes.MaxAsync(x => (int?)x.Order, ct) ?? 0;
        var levelType = new LevelType(context.OrganizationId!.Value, request!.NameAr, request.NameEn, max + 1, request.CanHostClasses);
        db.Add(levelType);
        await db.SaveChangesAsync(ct);
        return Results.Created(Routes.LevelType(levelType.Id), ToDto(levelType, 0));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, HttpContext http, LmsDbContext db, ICurrentContext context, SaveLevelTypeRequest? body, CancellationToken ct)
    {
        if (await http.CheckClientRootAsync(Permissions.LevelTypes.Update, ct) is { } refused)
        {
            return refused;
        }

        // Filtered set: another client's level type simply does not exist for the caller.
        var levelType = await db.LevelTypes.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (levelType is null)
        {
            return ApiProblems.NotFound();
        }

        var (request, problem) = LevelTypeValidators.Validate(body);
        if (problem is not null)
        {
            return problem;
        }

        if (await NameTakenAsync(db, request!, excludeId: id, ct) is { } taken)
        {
            return taken;
        }

        levelType.Update(request!.NameAr, request.NameEn, request.CanHostClasses);
        await db.SaveChangesAsync(ct);
        var counts = await UnitCountsAsync(db, context.OrganizationId!.Value, ct);
        return Results.Ok(ToDto(levelType, counts.GetValueOrDefault(id)));
    }

    private static async Task<IResult> ReorderAsync(
        HttpContext http, LmsDbContext db, ReorderLevelTypesRequest? body, CancellationToken ct)
    {
        if (await http.CheckClientRootAsync(Permissions.LevelTypes.Update, ct) is { } refused)
        {
            return refused;
        }

        var all = await db.LevelTypes.ToListAsync(ct);
        var ids = body?.OrderedIds ?? [];

        // Exactly the client's live ids, each once.
        if (ids.Count != all.Count || ids.Distinct().Count() != ids.Count || !ids.All(id => all.Any(x => x.Id == id)))
        {
            return LevelTypeValidators.ValidationFailed(new Dictionary<string, string[]> { ["orderedIds"] = ["order_must_list_every_level_type_once"] });
        }

        for (var i = 0; i < ids.Count; i++)
        {
            all.Single(x => x.Id == ids[i]).MoveTo(i + 1);
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, HttpContext http, LmsDbContext db, ICurrentContext context, CancellationToken ct)
    {
        if (await http.CheckClientRootAsync(Permissions.LevelTypes.Delete, ct) is { } refused)
        {
            return refused;
        }

        var levelType = await db.LevelTypes.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (levelType is null)
        {
            return ApiProblems.NotFound();
        }

        var counts = await UnitCountsAsync(db, context.OrganizationId!.Value, ct);
        if (counts.GetValueOrDefault(id) is var inUse and > 0)
        {
            return ApiProblems.Conflict(ProblemCodes.level_type_in_use, extra: new { unitCount = inUse });
        }

        db.Remove(levelType); // soft delete

        // Keep the order contiguous (1..n) for the remaining types.
        var order = 1;
        foreach (var other in await db.LevelTypes.Where(x => x.Id != id).OrderBy(x => x.Order).ToListAsync(ct))
        {
            other.MoveTo(order++);
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>A platform admin without a client context has no client whose level types could be meant.</summary>
    private static IResult? RequireClient(HttpContext http, ICurrentContext context) =>
        context.OrganizationId is null
            ? AccessProblem.From(AccessDecision.Deny(AccessDenialReason.MissingPermission), http)
            : null;

    /// <summary>409 name_taken when another live level type of the client has the same Arabic or English name.</summary>
    private static async Task<IResult?> NameTakenAsync(LmsDbContext db, SaveLevelTypeRequest request, Guid? excludeId, CancellationToken ct)
    {
        // The tenant filter already limits this to the caller's client and to live rows; the column collation makes
        // the comparison case- and accent-insensitive.
        var taken = await db.LevelTypes.AnyAsync(
            x => x.Id != excludeId && (x.NameAr == request.NameAr || x.NameEn == request.NameEn), ct);
        return taken ? ApiProblems.Conflict(ProblemCodes.name_taken) : null;
    }

    private static async Task<Dictionary<Guid, int>> UnitCountsAsync(LmsDbContext db, Guid organizationId, CancellationToken ct) =>
        // ISOLATION-BYPASS: level types are client-wide, so "in use" must count the live units of the whole client, not
        // only the caller's subtree; the client and soft-delete filters are re-applied explicitly.
        await db.OrgUnits.IgnoreQueryFilters()
            .Where(u => !u.IsDeleted && u.OrganizationId == organizationId)
            .GroupBy(u => u.LevelTypeId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

    private static LevelTypeDto ToDto(LevelType x, int unitCount) =>
        new(x.Id, x.NameAr, x.NameEn, x.Order, x.CanHostClasses, unitCount);
}
