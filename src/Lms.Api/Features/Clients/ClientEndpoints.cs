using Lms.Api.Infrastructure;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Clients;

/// <summary>Client administration. Every endpoint needs a platform-only permission (never held by a client role).</summary>
public static class ClientEndpoints
{
    public static IEndpointRouteBuilder MapClientEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.Clients, ListAsync).RequireAuthorization().RequirePermission(Permissions.Clients.View);
        app.MapGet(Routes.ClientPattern, GetAsync).RequireAuthorization().RequirePermission(Permissions.Clients.View);
        app.MapPost(Routes.Clients, CreateAsync).RequireAuthorization().RequirePermission(Permissions.Clients.Create);
        app.MapPut(Routes.ClientPattern, UpdateAsync).RequireAuthorization().RequirePermission(Permissions.Clients.Update);
        return app;
    }

    internal static ClientDto ToDto(Organization o, Guid rootOrgUnitId) => new(
        o.Id, o.NameAr, o.NameEn, o.Type.ToString(), o.Status.ToString(), rootOrgUnitId, o.CreatedAt, o.ShortCode);

    private static async Task<IResult> ListAsync(
        LmsDbContext db, int? page, int? pageSize, string? search, CancellationToken ct)
    {
        var pageNumber = page ?? 1;
        var size = pageSize ?? Paging.DefaultPageSize;
        var errors = ClientValidators.ValidatePaging(pageNumber, size);
        if (errors.Count > 0)
        {
            return ClientValidators.ToProblem(errors);
        }

        var query = db.Organizations.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(o => o.NameAr.Contains(term) || o.NameEn.Contains(term));
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(o => o.NameEn).ThenBy(o => o.Id)
            .Skip((pageNumber - 1) * size).Take(size)
            .Select(o => new
            {
                Organization = o,
                RootId = db.OrgUnits.Where(u => u.OrganizationId == o.Id && u.ParentId == null)
                    .Select(u => u.Id).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<ClientDto>(
            rows.Select(r => ToDto(r.Organization, r.RootId)).ToList(), total, pageNumber, size));
    }

    private static async Task<IResult> GetAsync(Guid id, LmsDbContext db, CancellationToken ct)
    {
        var dto = await FindAsync(db, id, ct);
        return dto is null ? ApiProblems.NotFound() : Results.Ok(dto);
    }

    private static async Task<IResult> CreateAsync(CreateClientRequest request, CreateClientHandler handler, CancellationToken ct)
    {
        var errors = ClientValidators.Validate(request);
        if (errors.Count > 0)
        {
            return ClientValidators.ToProblem(errors);
        }

        if (!Organization.IsValidShortCode(CreateClientHandler.ResolveShortCode(request)))
        {
            return ApiProblems.BadRequest(ProblemCodes.invalid_short_code);
        }

        try
        {
            var dto = await handler.HandleAsync(request, ct);
            return Results.Created(Routes.Client(dto.Id), dto);
        }
        catch (ShortCodeTakenException)
        {
            return ApiProblems.Conflict(ProblemCodes.short_code_taken);
        }
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateClientRequest request, LmsDbContext db, CancellationToken ct)
    {
        var errors = ClientValidators.Validate(request);
        if (errors.Count > 0)
        {
            return ClientValidators.ToProblem(errors);
        }

        var organization = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (organization is null)
        {
            return ApiProblems.NotFound();
        }

        organization.Rename(request.NameAr.Trim(), request.NameEn.Trim());
        organization.SetStatus(Enum.Parse<OrganizationStatus>(request.Status));
        await db.SaveChangesAsync(ct);

        return Results.Ok(await FindAsync(db, id, ct));
    }

    private static async Task<ClientDto?> FindAsync(LmsDbContext db, Guid id, CancellationToken ct)
    {
        var row = await db.Organizations.Where(o => o.Id == id)
            .Select(o => new
            {
                Organization = o,
                RootId = db.OrgUnits.Where(u => u.OrganizationId == o.Id && u.ParentId == null)
                    .Select(u => u.Id).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : ToDto(row.Organization, row.RootId);
    }
}