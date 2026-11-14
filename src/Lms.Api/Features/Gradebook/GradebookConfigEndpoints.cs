using Lms.Api.Infrastructure;
using Lms.Domain.Grading;
using Lms.Infrastructure.Grading.Gradebooks;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;
using GradebookEntity = Lms.Domain.Grading.Gradebook;

namespace Lms.Api.Features.Gradebook;

/// <summary>Configuration, categories and manual items of a gradebook (<c>Grades.Manage</c>; <c>IGradebookWriter</c> guards the lock and a closed class or year).</summary>
public static class GradebookConfigEndpoints
{
    public static IEndpointRouteBuilder MapGradebookConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut(Routes.ClassCourseGradebookConfigPattern, ConfigAsync).RequirePermission(Permissions.Grades.Manage);
        app.MapPut(Routes.ClassCourseGradebookCategoriesPattern, CategoriesAsync).RequirePermission(Permissions.Grades.Manage);
        app.MapPost(Routes.ClassCourseGradebookItemsPattern, CreateItemAsync).RequirePermission(Permissions.Grades.Manage);
        app.MapPut(Routes.ClassCourseGradebookItemPattern, UpdateItemAsync).RequirePermission(Permissions.Grades.Manage);
        app.MapDelete(Routes.ClassCourseGradebookItemPattern, DeleteItemAsync).RequirePermission(Permissions.Grades.Manage);
        return app;
    }

    private static async Task<IResult> ConfigAsync(
        Guid id, SaveGradebookConfigRequest? request, Guid? termId, HttpContext http, GradebookReader reader, GradebookEditor editor,
        IScaleResolver scales, LmsDbContext db, CancellationToken ct)
    {
        if (request is null)
        {
            return GradebookValidators.Invalid("Body", "required");
        }

        var (target, problem) = await GradebookAccess.ForWriteAsync(http, id, termId, reader, ct);
        if (problem is not null)
        {
            return problem;
        }

        try
        {
            await editor.ConfigureAsync(target!.ClassCourse, target.Gradebook, request, ct);
        }
        catch (GradebookRuleException rule)
        {
            return GradebookValidators.From(rule);
        }

        var unit = await db.OrgUnits.FirstAsync(u => u.Id == target.ClassCourse.OrgUnitId, ct);
        var scale = request.ScaleId is { } sid ? await scales.FindScaleAsync(unit, sid, ct) : null;
        var gb = target.Gradebook;
        return Results.Ok(new GradebookConfigDto(
            gb.GradingScaleId, scale?.Scale.NameAr, scale?.Scale.NameEn, scale?.Scale.Provisional ?? false, gb.DecimalPlaces, gb.CategoryMethod,
            gb.MissingPolicy, gb.RoundingMode));
    }

    private static async Task<IResult> CategoriesAsync(
        Guid id, SaveGradeCategoriesRequest? request, Guid? termId, HttpContext http, GradebookReader reader, GradebookEditor editor,
        LmsDbContext db, CancellationToken ct)
    {
        if (GradebookValidators.ValidateCategories(request) is { } invalid)
        {
            return invalid;
        }

        var (target, problem) = await GradebookAccess.ForWriteAsync(http, id, termId, reader, ct);
        if (problem is not null)
        {
            return problem;
        }

        try
        {
            await editor.SaveCategoriesAsync(
                target!.Gradebook,
                [.. request!.Categories.Select(c => new CategorySpec(c.Id, c.NameAr.Trim(), c.NameEn.Trim(), c.Weight, c.Order))], ct);
        }
        catch (GradebookRuleException rule)
        {
            return GradebookValidators.From(rule);
        }

        return Results.Ok(await db.Set<GradeCategory>().Where(c => c.GradebookId == target.Gradebook.Id).OrderBy(c => c.Order).ThenBy(c => c.Id)
            .Select(c => new GradeCategoryDto(c.Id, c.NameAr, c.NameEn, c.Weight, c.Order, c.IsUncategorised)).ToListAsync(ct));
    }

    private static async Task<IResult> CreateItemAsync(
        Guid id, CreateGradeItemRequest? request, Guid? termId, HttpContext http, GradebookReader reader, GradebookEditor editor, CancellationToken ct)
    {
        if (request is null)
        {
            return GradebookValidators.Invalid("Body", "required");
        }

        if (GradebookValidators.ValidateItem(request.TitleAr, request.TitleEn, request.MaxPoints) is { } invalid)
        {
            return invalid;
        }

        var (target, problem) = await GradebookAccess.ForWriteAsync(http, id, termId, reader, ct);
        if (problem is not null)
        {
            return problem;
        }

        try
        {
            var item = await editor.CreateItemAsync(target!.Gradebook, request, ct);
            return Results.Created(Routes.ClassCourseGradebookItem(id, item.Id), GradebookReader.ToDto(item));
        }
        catch (GradebookRuleException rule)
        {
            return GradebookValidators.From(rule);
        }
    }

    private static async Task<IResult> UpdateItemAsync(
        Guid id, Guid iid, UpdateGradeItemRequest? request, HttpContext http, GradebookReader reader, GradebookEditor editor, LmsDbContext db,
        CancellationToken ct)
    {
        if (request is null)
        {
            return GradebookValidators.Invalid("Body", "required");
        }

        if (GradebookValidators.ValidateItem(request.TitleAr, request.TitleEn, request.MaxPoints) is { } invalid)
        {
            return invalid;
        }

        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Grades.Manage, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        var item = await ItemOfAsync(db, cc!.Id, iid, ct);
        if (item is null)
        {
            return ApiProblems.NotFound();
        }

        try
        {
            var gradebook = await db.Set<GradebookEntity>().SingleAsync(g => g.Id == item.GradebookId, ct);
            await editor.UpdateItemAsync(gradebook, item, request, ct);
            return Results.Ok(GradebookReader.ToDto(item));
        }
        catch (GradebookRuleException rule)
        {
            return GradebookValidators.From(rule);
        }
    }

    private static async Task<IResult> DeleteItemAsync(
        Guid id, Guid iid, HttpContext http, GradebookEditor editor, LmsDbContext db, Lms.Domain.Abstractions.ICurrentContext context, CancellationToken ct)
    {
        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Grades.Manage, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        var item = await ItemOfAsync(db, cc!.Id, iid, ct);
        if (item is null)
        {
            return ApiProblems.NotFound();
        }

        try
        {
            await editor.DeleteItemAsync(await db.Set<GradebookEntity>().SingleAsync(g => g.Id == item.GradebookId, ct), item, context.UserId, ct);
            return Results.NoContent();
        }
        catch (GradebookRuleException rule)
        {
            return GradebookValidators.From(rule);
        }
    }

    /// <summary>The item when it sits in a gradebook of this class course (an item of another class course is a miss).</summary>
    private static async Task<GradeItem?> ItemOfAsync(LmsDbContext db, Guid classCourseId, Guid itemId, CancellationToken ct) =>
        await (from i in db.Set<GradeItem>()
               join g in db.Set<GradebookEntity>() on i.GradebookId equals g.Id
               where i.Id == itemId && g.ClassCourseId == classCourseId
               select i).FirstOrDefaultAsync(ct);
}
