using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Grading;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Grading.Exchange;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;
using GradebookEntity = Lms.Domain.Grading.Gradebook;

namespace Lms.Api.Features.Gradebook;

/// <summary>
/// Column export, import preview and import apply of one grade item (M5 Task 10, L9; <c>Grades.Manage</c>; a teacher's own class courses, 404 otherwise).
/// An item of another class course is a miss; only a manual item can be imported; the upload must be a plain <c>.xlsx</c> within <c>Files.MaxUploadMb</c>.
/// </summary>
public static class GradeExchangeEndpoints
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IEndpointRouteBuilder MapGradeExchangeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.ClassCourseGradebookItemExportPattern, ExportAsync).RequirePermission(Permissions.Grades.Manage);
        app.MapPost(Routes.ClassCourseGradebookItemImportPreviewPattern, PreviewAsync).RequirePermission(Permissions.Grades.Manage);
        app.MapPost(Routes.ClassCourseGradebookItemImportApplyPattern, ApplyAsync).RequirePermission(Permissions.Grades.Manage);
        return app;
    }

    private static async Task<IResult> ExportAsync(
        Guid id, Guid iid, HttpContext http, LmsDbContext db, GradeColumnExchange exchange, ICurrentContext context, CancellationToken ct)
    {
        var (cc, item, problem) = await TargetAsync(http, db, id, iid, ct);
        if (problem is not null)
        {
            return problem;
        }

        var bytes = await exchange.ExportAsync(cc!, item!, context.UserId, ct);
        return Results.File(bytes, XlsxContentType, $"grades-{item!.Id:N}.xlsx");
    }

    private static async Task<IResult> PreviewAsync(
        Guid id, Guid iid, HttpContext http, LmsDbContext db, GradeColumnExchange exchange, ISettingsResolver settings, ICurrentContext context,
        CancellationToken ct)
    {
        var (cc, item, problem) = await TargetAsync(http, db, id, iid, ct);
        if (problem is not null)
        {
            return problem;
        }

        var (form, formProblem) = await UploadForm.ReadAsync(http, ct);
        var file = form?.Files.FirstOrDefault();
        if (formProblem is not null || file is null || file.Length == 0 || !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return ApiProblems.BadRequest(ProblemCodes.grade_import_invalid); // .xlsx only: .xlsm, .xls and anything else are refused here
        }

        var maxMb = (await settings.ResolveAsync(SettingKeys.FilesMaxUploadMb, cc!.OrgUnitPath, ct)).Value;
        var maxBytes = maxMb * 1024L * 1024L;
        if (file.Length > maxBytes)
        {
            return ApiProblems.BadRequest(ProblemCodes.grade_import_invalid);
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        try
        {
            return Results.Ok(await exchange.PreviewAsync(cc, item!, buffer.ToArray(), maxBytes, context.UserId!.Value, ct));
        }
        catch (GradebookRuleException rule)
        {
            return GradebookValidators.From(rule);
        }
    }

    private static async Task<IResult> ApplyAsync(
        Guid id, Guid iid, ApplyGradeImportRequest? request, HttpContext http, LmsDbContext db, GradeColumnExchange exchange, ICurrentContext context,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Token))
        {
            return GradebookValidators.Invalid("Token", "required");
        }

        var (cc, item, problem) = await TargetAsync(http, db, id, iid, ct);
        if (problem is not null)
        {
            return problem;
        }

        try
        {
            return Results.Ok(await exchange.ApplyAsync(cc!, item!, request.Token, context.UserId!.Value, ct));
        }
        catch (GradebookRuleException rule)
        {
            return GradebookValidators.From(rule);
        }
    }

    /// <summary>The class course (permission and relationship) and the item when it sits in a gradebook of that class course.</summary>
    private static async Task<(Lms.Domain.Classes.ClassCourse? Cc, GradeItem? Item, IResult? Problem)> TargetAsync(
        HttpContext http, LmsDbContext db, Guid classCourseId, Guid itemId, CancellationToken ct)
    {
        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Grades.Manage, classCourseId, ct);
        if (problem is not null)
        {
            return (null, null, problem);
        }

        var item = await (from i in db.Set<GradeItem>()
                          join g in db.Set<GradebookEntity>() on i.GradebookId equals g.Id
                          where i.Id == itemId && g.ClassCourseId == cc!.Id
                          select i).FirstOrDefaultAsync(ct);
        return item is null ? (null, null, ApiProblems.NotFound()) : (cc, item, null);
    }
}
