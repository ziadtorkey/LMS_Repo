using Lms.Api.Infrastructure;
using Lms.Domain.Grading;
using Lms.Infrastructure.Grading.Scales;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.GradingScales;

/// <summary>Weight templates of an org unit: reads (<c>Grades.View</c>) include the ancestors', writes (<c>Grades.ConfigureScales</c>) the unit's own.</summary>
public static class WeightTemplateEndpoints
{
    public static IEndpointRouteBuilder MapWeightTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.OrgUnitWeightTemplatesPattern, ListAsync).RequirePermission(Permissions.Grades.View);
        app.MapPost(Routes.OrgUnitWeightTemplatesPattern, CreateAsync).RequirePermission(Permissions.Grades.ConfigureScales);
        app.MapGet(Routes.WeightTemplatePattern, GetAsync).RequirePermission(Permissions.Grades.View);
        app.MapPut(Routes.WeightTemplatePattern, UpdateAsync).RequirePermission(Permissions.Grades.ConfigureScales);
        app.MapDelete(Routes.WeightTemplatePattern, DeleteAsync).RequirePermission(Permissions.Grades.ConfigureScales);
        return app;
    }

    private static async Task<IResult> ListAsync(Guid id, HttpContext http, IScaleResolver resolver, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Grades.View, id, ct);
        return access.Problem ?? Results.Ok((await resolver.TemplatesAsync(access.Unit!, ct)).Select(t => GradingScaleMapper.ToDto(t, id)).ToList());
    }

    private static async Task<IResult> GetAsync(Guid id, Guid tid, HttpContext http, IScaleResolver resolver, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Grades.View, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var template = await resolver.FindTemplateAsync(access.Unit!, tid, ct);
        return template is null ? ApiProblems.NotFound() : Results.Ok(GradingScaleMapper.ToDto(template, id));
    }

    private static async Task<IResult> CreateAsync(
        Guid id, SaveWeightTemplateRequest request, HttpContext http, GradingScaleService service, IScaleResolver resolver, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Grades.ConfigureScales, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var (items, problem) = GradingScaleValidators.ValidateTemplate(request);
        if (problem is not null)
        {
            return problem;
        }

        var template = await service.SaveTemplateAsync(access.Unit!, null, request.NameAr.Trim(), request.NameEn.Trim(), items!, ct);
        return Results.Created(Routes.WeightTemplate(id, template.Id), GradingScaleMapper.ToDto((await resolver.FindTemplateAsync(access.Unit!, template.Id, ct))!, id));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, Guid tid, SaveWeightTemplateRequest request, HttpContext http, LmsDbContext db, GradingScaleService service,
        IScaleResolver resolver, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Grades.ConfigureScales, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var template = await db.Set<WeightTemplate>().FirstOrDefaultAsync(t => t.Id == tid && t.OrgUnitId == id, ct);
        if (template is null)
        {
            return ApiProblems.NotFound();
        }

        var (items, problem) = GradingScaleValidators.ValidateTemplate(request);
        if (problem is not null)
        {
            return problem;
        }

        await service.SaveTemplateAsync(access.Unit!, template, request.NameAr.Trim(), request.NameEn.Trim(), items!, ct);
        return Results.Ok(GradingScaleMapper.ToDto((await resolver.FindTemplateAsync(access.Unit!, tid, ct))!, id));
    }

    private static async Task<IResult> DeleteAsync(Guid id, Guid tid, HttpContext http, LmsDbContext db, GradingScaleService service, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Grades.ConfigureScales, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var template = await db.Set<WeightTemplate>().FirstOrDefaultAsync(t => t.Id == tid && t.OrgUnitId == id, ct);
        if (template is null)
        {
            return ApiProblems.NotFound();
        }

        await service.DeleteTemplateAsync(template, ct);
        return Results.NoContent();
    }
}
