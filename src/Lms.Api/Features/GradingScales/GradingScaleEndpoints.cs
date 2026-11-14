using Lms.Api.Infrastructure;
using Lms.Domain.Grading;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Grading.Scales;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.GradingScales;

/// <summary>
/// Scales of an org unit. Reads (<c>Grades.View</c>) include the scales of the unit's ancestors, read only; writes (<c>Grades.ConfigureScales</c>)
/// touch the unit's own scales only (an inherited scale or a preset template answers 404 to a write).
/// </summary>
public static class GradingScaleEndpoints
{
    public static IEndpointRouteBuilder MapGradingScaleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.OrgUnitGradingScalesPattern, ListAsync).RequirePermission(Permissions.Grades.View);
        app.MapPost(Routes.OrgUnitGradingScalesPattern, CreateAsync).RequirePermission(Permissions.Grades.ConfigureScales);
        app.MapGet(Routes.GradingScalePattern, GetAsync).RequirePermission(Permissions.Grades.View);
        app.MapPut(Routes.GradingScalePattern, UpdateAsync).RequirePermission(Permissions.Grades.ConfigureScales);
        app.MapDelete(Routes.GradingScalePattern, DeleteAsync).RequirePermission(Permissions.Grades.ConfigureScales);
        app.MapPost(Routes.GradingScaleConfirmPattern, ConfirmAsync).RequirePermission(Permissions.Grades.ConfigureScales);
        app.MapPost(Routes.GradingScaleDefaultPattern, DefaultAsync).RequirePermission(Permissions.Grades.ConfigureScales);
        app.MapPost(Routes.OrgUnitGradingScalePresetsPattern, PresetAsync).RequirePermission(Permissions.Grades.ConfigureScales);
        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid id, HttpContext http, IScaleResolver resolver, IScalePresetCatalog catalog, IScaleUsage usage, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Grades.View, id, ct);
        return access.Problem
            ?? Results.Ok(await GradingScaleMapper.ToDtosAsync(await resolver.ScalesAsync(access.Unit!, ct), id, catalog, usage, ct));
    }

    private static async Task<IResult> GetAsync(
        Guid id, Guid sid, HttpContext http, IScaleResolver resolver, IScalePresetCatalog catalog, IScaleUsage usage, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Grades.View, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var scale = await resolver.FindScaleAsync(access.Unit!, sid, ct);
        return scale is null ? ApiProblems.NotFound() : Results.Ok(await GradingScaleMapper.ToDtoAsync(scale, id, catalog, usage, ct));
    }

    private static async Task<IResult> CreateAsync(
        Guid id, SaveGradingScaleRequest request, HttpContext http, GradingScaleService service, IScaleResolver resolver,
        IScalePresetCatalog catalog, IScaleUsage usage, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Grades.ConfigureScales, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var (bands, problem) = GradingScaleValidators.ValidateScale(request);
        if (problem is not null)
        {
            return problem;
        }

        var scale = await service.CreateAsync(access.Unit!, request.NameAr.Trim(), request.NameEn.Trim(), request.Kind, bands!, ct);
        var dto = await GradingScaleMapper.ToDtoAsync((await resolver.FindScaleAsync(access.Unit!, scale.Id, ct))!, id, catalog, usage, ct);
        return Results.Created(Routes.GradingScale(id, scale.Id), dto);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, Guid sid, SaveGradingScaleRequest request, HttpContext http, LmsDbContext db, GradingScaleService service,
        IScaleResolver resolver, IScalePresetCatalog catalog, IScaleUsage usage, CancellationToken ct)
    {
        var (unit, scale, problem) = await OwnAsync(http, db, id, sid, ct);
        if (problem is not null)
        {
            return problem;
        }

        var (bands, invalid) = GradingScaleValidators.ValidateScale(request);
        if (invalid is not null)
        {
            return invalid;
        }

        await service.UpdateAsync(scale!, request.NameAr.Trim(), request.NameEn.Trim(), request.Kind, bands!, ct);
        return Results.Ok(await GradingScaleMapper.ToDtoAsync((await resolver.FindScaleAsync(unit!, sid, ct))!, id, catalog, usage, ct));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, Guid sid, HttpContext http, LmsDbContext db, GradingScaleService service, CancellationToken ct)
    {
        var (_, scale, problem) = await OwnAsync(http, db, id, sid, ct);
        if (problem is not null)
        {
            return problem;
        }

        return await service.DeleteAsync(scale!, ct) ? Results.NoContent() : ApiProblems.Conflict(ProblemCodes.scale_in_use);
    }

    private static async Task<IResult> ConfirmAsync(
        Guid id, Guid sid, HttpContext http, LmsDbContext db, GradingScaleService service, IScaleResolver resolver,
        IScalePresetCatalog catalog, IScaleUsage usage, CancellationToken ct)
    {
        var (unit, scale, problem) = await OwnAsync(http, db, id, sid, ct);
        if (problem is not null)
        {
            return problem;
        }

        await service.ConfirmAsync(scale!, ct);
        return Results.Ok(await GradingScaleMapper.ToDtoAsync((await resolver.FindScaleAsync(unit!, sid, ct))!, id, catalog, usage, ct));
    }

    private static async Task<IResult> DefaultAsync(
        Guid id, Guid sid, HttpContext http, LmsDbContext db, GradingScaleService service, IScaleResolver resolver,
        IScalePresetCatalog catalog, IScaleUsage usage, CancellationToken ct)
    {
        var (unit, scale, problem) = await OwnAsync(http, db, id, sid, ct);
        if (problem is not null)
        {
            return problem;
        }

        await service.MakeDefaultAsync(scale!, ct);
        return Results.Ok(await GradingScaleMapper.ToDtoAsync((await resolver.FindScaleAsync(unit!, sid, ct))!, id, catalog, usage, ct));
    }

    private static async Task<IResult> PresetAsync(
        Guid id, CopyScalePresetRequest request, HttpContext http, GradingScaleService service, IScaleResolver resolver,
        IScalePresetCatalog catalog, IScaleUsage usage, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Grades.ConfigureScales, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.SeedKey))
        {
            return GradingScaleValidators.Invalid("SeedKey", "required");
        }

        var scale = await service.CopyPresetAsync(access.Unit!, request.SeedKey, ct);
        if (scale is null)
        {
            return ApiProblems.NotFound();
        }

        var dto = await GradingScaleMapper.ToDtoAsync((await resolver.FindScaleAsync(access.Unit!, scale.Id, ct))!, id, catalog, usage, ct);
        return Results.Created(Routes.GradingScale(id, scale.Id), dto);
    }

    /// <summary>The scale the caller may change: owned by this very unit and not a preset template; anything else is 404.</summary>
    private static async Task<(OrgUnit? Unit, GradingScale? Scale, IResult? Problem)> OwnAsync(
        HttpContext http, LmsDbContext db, Guid unitId, Guid scaleId, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Grades.ConfigureScales, unitId, ct);
        if (access.Problem is not null)
        {
            return (null, null, access.Problem);
        }

        var scale = await db.Set<GradingScale>().FirstOrDefaultAsync(s => s.Id == scaleId && s.OrgUnitId == unitId && !s.IsTemplate, ct);
        return scale is null ? (null, null, ApiProblems.NotFound()) : (access.Unit, scale, null);
    }
}
