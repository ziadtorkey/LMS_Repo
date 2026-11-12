using Lms.Api.Infrastructure;
using Lms.Domain.Access;
using Lms.Domain.Courses;
using Lms.Domain.Library;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Library;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Library;

/// <summary>
/// The digital library (FR-LIB-01, D13): browse (the unit and everything inherited from its ancestors), tags, one resource,
/// create, edit and delete at the owning unit (Library.Manage there; Library.* is not relationship-limited, a Teacher holds it
/// unit-wide). An inherited resource is read-only: edits go through the owner unit.
/// </summary>
public static class LibraryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.Library, ListAsync).RequireAuthorization().RequirePermission(Permissions.Library.View);
        // Registered before the {id} route so "tags" is never read as an id.
        app.MapGet(Routes.LibraryTags, TagsAsync).RequireAuthorization().RequirePermission(Permissions.Library.View);
        app.MapPost(Routes.Library, CreateAsync).RequireAuthorization().RequirePermission(Permissions.Library.Manage);
        app.MapGet(Routes.LibraryResourcePattern, GetAsync).RequireAuthorization().RequirePermission(Permissions.Library.View);
        app.MapPut(Routes.LibraryResourcePattern, UpdateAsync).RequireAuthorization().RequirePermission(Permissions.Library.Manage);
        app.MapDelete(Routes.LibraryResourcePattern, DeleteAsync).RequireAuthorization().RequirePermission(Permissions.Library.Manage);
        LibraryReaderEndpoints.Map(app);
    }

    private static async Task<IResult> ListAsync(
        [AsParameters] LibraryQuery query, HttpContext http, LibraryQueries queries, LibraryMapper mapper, IAccessEvaluator access, CancellationToken ct)
    {
        if (query.Page < 1 || query.PageSize < 1 || query.PageSize > Paging.MaxPageSize)
        {
            return LibrarySupport.Invalid(query.Page < 1 ? "page" : "pageSize", "paging_invalid");
        }

        var (unit, problem) = await BrowseUnitAsync(query.OrgUnitId, http, queries, ct);
        if (problem is not null)
        {
            return problem;
        }

        var filtered = queries.Apply(queries.VisibleAt(unit!), query, await queries.StudentLevelTypesAsync(ct), unit!.OrganizationId);
        var ordered = filtered.OrderBy(r => r.TitleEn).ThenBy(r => r.TitleAr).ThenBy(r => r.Id);
        var total = await ordered.CountAsync(ct);
        var rows = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var canManage = (await access.EvaluateAsync(Permissions.Library.Manage, unit.Path, ct)).Allowed;
        return Results.Ok(new PagedResult<LibraryResourceDto>(await mapper.ToDtosAsync(rows, unit, canManage, ct), total, query.Page, query.PageSize));
    }

    private static async Task<IResult> TagsAsync(
        Guid? orgUnitId, HttpContext http, LibraryQueries queries, CancellationToken ct)
    {
        var (unit, problem) = await BrowseUnitAsync(orgUnitId, http, queries, ct);
        if (problem is not null)
        {
            return problem;
        }

        var levels = await queries.StudentLevelTypesAsync(ct);
        var visible = queries.Apply(queries.VisibleAt(unit!), new LibraryQuery(), levels, unit!.OrganizationId).Select(r => r.Id);
        var tags = await queries.Tags(unit.OrganizationId).Where(t => visible.Contains(t.ResourceId)).Select(t => t.Tag).Distinct().OrderBy(t => t).ToListAsync(ct);
        return Results.Ok(tags);
    }

    private static async Task<IResult> GetAsync(
        Guid id, HttpContext http, LibraryQueries queries, LibraryMapper mapper, IAccessEvaluator access, CancellationToken ct)
    {
        var (resource, unit, problem) = await FindVisibleAsync(id, http, queries, ct);
        if (problem is not null)
        {
            return problem;
        }

        var canManage = (await access.EvaluateAsync(Permissions.Library.Manage, unit!.Path, ct)).Allowed;
        return Results.Ok((await mapper.ToDtosAsync([resource!], unit, canManage, ct))[0]);
    }

    private static async Task<IResult> CreateAsync(
        SaveLibraryResourceRequest? request, HttpContext http, LmsDbContext db, ICourseCatalog catalog, LibraryQueries queries,
        LibraryMapper mapper, CancellationToken ct)
    {
        if (request is null)
        {
            return LibrarySupport.Invalid("body", "required");
        }

        var access = await http.CheckUnitAsync(Permissions.Library.Manage, request.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        if (await LibrarySupport.ValidateAsync(request, unit, db, catalog, ct) is { } invalid)
        {
            return invalid;
        }

        var resource = new LibraryResource(
            unit, request.TitleAr, request.TitleEn, request.DescriptionAr, request.DescriptionEn, request.Type, request.CourseId,
            request.CoverAssetId, request.FileAssetId, request.Url, request.DownloadAllowed);
        db.Set<LibraryResource>().Add(resource);
        AddChildren(db, resource, request);
        await db.SaveChangesAsync(ct);
        return Results.Created(Routes.LibraryResource(resource.Id), (await mapper.ToDtosAsync([resource], unit, true, ct))[0]);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, SaveLibraryResourceRequest? request, HttpContext http, LmsDbContext db, ICourseCatalog catalog, LibraryMapper mapper,
        CancellationToken ct)
    {
        var (resource, unit, problem) = await LoadOwnedAsync(id, http, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (request is null || request.OrgUnitId != resource!.OrgUnitId)
        {
            return LibrarySupport.Invalid(request is null ? "body" : "orgUnitId", request is null ? "required" : "owner_cannot_change");
        }

        if (await LibrarySupport.ValidateAsync(request, unit!, db, catalog, ct) is { } invalid)
        {
            return invalid;
        }

        resource.Update(
            request.TitleAr, request.TitleEn, request.DescriptionAr, request.DescriptionEn, request.Type, request.CourseId,
            request.CoverAssetId, request.FileAssetId, request.Url, request.DownloadAllowed);
        db.Set<LibraryTag>().RemoveRange(await db.Set<LibraryTag>().Where(t => t.ResourceId == id).ToListAsync(ct));
        db.Set<LibraryResourceLevel>().RemoveRange(await db.Set<LibraryResourceLevel>().Where(l => l.ResourceId == id).ToListAsync(ct));
        AddChildren(db, resource, request);
        await db.SaveChangesAsync(ct);
        return Results.Ok((await mapper.ToDtosAsync([resource], unit!, true, ct))[0]);
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var (resource, _, problem) = await LoadOwnedAsync(id, http, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        db.Set<LibraryTag>().RemoveRange(await db.Set<LibraryTag>().Where(t => t.ResourceId == id).ToListAsync(ct));
        db.Set<LibraryResourceLevel>().RemoveRange(await db.Set<LibraryResourceLevel>().Where(l => l.ResourceId == id).ToListAsync(ct));
        db.Set<LibraryResource>().Remove(resource!);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static void AddChildren(LmsDbContext db, LibraryResource resource, SaveLibraryResourceRequest request)
    {
        db.Set<LibraryTag>().AddRange(LibrarySupport.CleanTags(request.Tags)!.Select(t => new LibraryTag(resource, t)));
        db.Set<LibraryResourceLevel>().AddRange((request.LevelTypeIds ?? []).Distinct().Select(l => new LibraryResourceLevel(resource, l)));
    }

    /// <summary>The unit to browse (default: the caller's context unit); the caller must see it and hold Library.View there.</summary>
    internal static async Task<(OrgUnit? Unit, IResult? Problem)> BrowseUnitAsync(
        Guid? orgUnitId, HttpContext http, LibraryQueries queries, CancellationToken ct)
    {
        var unitId = orgUnitId ?? (await queries.ContextUnitAsync(ct))?.Id;
        if (unitId is not { } id)
        {
            return (null, ApiProblems.NotFound());
        }

        var access = await http.CheckUnitAsync(Permissions.Library.View, id, ct);
        return access.Problem is null ? (access.Unit, null) : (null, access.Problem);
    }

    /// <summary>A resource the caller can read at the context unit (owned there or above, and meant for their level).</summary>
    internal static async Task<(LibraryResource? Resource, OrgUnit? Unit, IResult? Problem)> FindVisibleAsync(
        Guid id, HttpContext http, LibraryQueries queries, CancellationToken ct)
    {
        var (unit, problem) = await BrowseUnitAsync(null, http, queries, ct);
        if (problem is not null)
        {
            return (null, null, problem);
        }

        var visible = queries.Apply(queries.VisibleAt(unit!), new LibraryQuery(), await queries.StudentLevelTypesAsync(ct), unit!.OrganizationId);
        var resource = await visible.FirstOrDefaultAsync(r => r.Id == id, ct);
        return resource is null ? (null, null, ApiProblems.NotFound()) : (resource, unit, null);
    }

    /// <summary>A resource owned inside the caller's scope (the normal filter), with Library.Manage at its owner unit.</summary>
    private static async Task<(LibraryResource? Resource, OrgUnit? Unit, IResult? Problem)> LoadOwnedAsync(
        Guid id, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var resource = await db.Set<LibraryResource>().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (resource is null)
        {
            return (null, null, ApiProblems.NotFound());
        }

        var access = await http.CheckUnitAsync(Permissions.Library.Manage, resource.OrgUnitId, ct);
        return access.Problem is null ? (resource, access.Unit, null) : (null, null, access.Problem);
    }
}
