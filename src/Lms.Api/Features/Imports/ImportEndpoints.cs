using System.Globalization;
using Lms.Api.Infrastructure;
using Lms.Domain.Files;
using Lms.Domain.Imports;
using Lms.Infrastructure.Imports;
using Lms.Shared;
using Lms.Shared.Imports;
using Microsoft.AspNetCore.Http.Features;

namespace Lms.Api.Features.Imports;

/// <summary>
/// Endpoints of the import framework, mapped once per registered definition with its kind as a literal route segment so
/// every route carries the permission of its kind. The first gate is the permission; the unit (upload) or the batch's own
/// unit (everything else) is checked next, and anything outside the caller's scope is 404.
/// </summary>
public static class ImportEndpoints
{
    private const long TransportLimit = ImportLimits.MaxFileBytes + 1024 * 1024;

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        using var scope = app.ServiceProvider.CreateScope();
        var definitions = scope.ServiceProvider.GetRequiredService<ImportDefinitions>().All;
        foreach (var definition in definitions)
        {
            Map(app, definition.Kind, definition.Permission);
        }

        return app;
    }

    private static string Literal(string pattern, string kind) => pattern.Replace("{kind}", kind, StringComparison.Ordinal);

    private static void Map(IEndpointRouteBuilder app, string kind, string permission)
    {
        app.MapGet(Literal(Routes.ImportColumnsPattern, kind), (ImportDefinitions all) => ColumnsAsync(all, kind))
            .RequireAuthorization().RequirePermission(permission);
        app.MapGet(Literal(Routes.ImportTemplatePattern, kind), (ImportDefinitions all, string? culture) => Template(all, kind, culture))
            .RequireAuthorization().RequirePermission(permission);
        app.MapPost(Literal(Routes.ImportKindPattern, kind), (HttpContext http, ImportDefinitions all, ImportService service, CancellationToken ct) =>
                UploadAsync(http, all, service, kind, permission, ct))
            .RequireAuthorization().RequirePermission(permission);
        app.MapGet(Literal(Routes.ImportKindPattern, kind), (ImportQueries queries, int? page, int? pageSize, CancellationToken ct) =>
                ListAsync(queries, kind, page, pageSize, ct))
            .RequireAuthorization().RequirePermission(permission);
        app.MapGet(Literal(Routes.ImportBatchPattern, kind), (Guid id, HttpContext http, ImportQueries queries, CancellationToken ct) =>
                GetAsync(http, queries, kind, permission, id, ct))
            .RequireAuthorization().RequirePermission(permission);
        app.MapGet(Literal(Routes.ImportRowsPattern, kind),
                (Guid id, HttpContext http, ImportQueries queries, bool? errorsOnly, string? culture, int? page, int? pageSize, CancellationToken ct) =>
                    RowsAsync(http, queries, kind, permission, id, errorsOnly ?? false, culture, page, pageSize, ct))
            .RequireAuthorization().RequirePermission(permission);
        app.MapGet(Literal(Routes.ImportErrorReportPattern, kind),
                (Guid id, HttpContext http, ImportQueries queries, IFileStorage files, CancellationToken ct) =>
                    ErrorReportAsync(http, queries, files, kind, permission, id, ct))
            .RequireAuthorization().RequirePermission(permission);
        app.MapPost(Literal(Routes.ImportCommitPattern, kind),
                (Guid id, HttpContext http, ImportQueries queries, ImportService service, CancellationToken ct) =>
                    CommitAsync(http, queries, service, kind, permission, id, ct))
            .RequireAuthorization().RequirePermission(permission);
        app.MapDelete(Literal(Routes.ImportBatchPattern, kind),
                (Guid id, HttpContext http, ImportQueries queries, ImportService service, CancellationToken ct) =>
                    DiscardAsync(http, queries, service, kind, permission, id, ct))
            .RequireAuthorization().RequirePermission(permission);
    }

    private static IResult ColumnsAsync(ImportDefinitions all, string kind) =>
        Results.Ok(all.Find(kind)!.Columns
            .Select(c => new ImportColumnDto(c.Key, c.HeaderAr, c.HeaderEn, c.Required, c.AllowedValues, c.Sensitive)).ToList());

    private static IResult Template(ImportDefinitions all, string kind, string? culture)
    {
        var buffer = new MemoryStream();
        ImportTemplateWriter.Write(all.Find(kind)!, culture ?? CurrentCulture(), buffer);
        buffer.Position = 0;
        return Results.File(buffer, FileContentTypes.ForExtension(".xlsx"), $"{kind}-template.xlsx");
    }

    private static async Task<IResult> UploadAsync(
        HttpContext http, ImportDefinitions all, ImportService service, string kind, string permission, CancellationToken ct)
    {
        var request = http.Request;
        if (!request.HasFormContentType)
        {
            return ApiProblems.BadRequest(ProblemCodes.import_file_invalid);
        }

        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = TransportLimit;
        }

        IFormCollection form;
        try
        {
            http.Features.Set<IFormFeature>(new FormFeature(request, new FormOptions { MultipartBodyLengthLimit = TransportLimit }));
            form = await request.ReadFormAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException or IOException)
        {
            return ApiProblems.BadRequest(ProblemCodes.import_file_invalid);
        }

        var file = form.Files.GetFile("file");
        if (!Guid.TryParse(form["orgUnitId"], out var orgUnitId) || file is null)
        {
            return ApiProblems.BadRequest(ProblemCodes.import_file_invalid);
        }

        var access = await http.CheckUnitAsync(permission, orgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var fileName = Path.GetFileName(file.FileName);
        if (file.Length is 0 or > ImportLimits.MaxFileBytes || fileName.Length is 0 or > 255
            || !string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return ApiProblems.BadRequest(ProblemCodes.import_file_invalid);
        }

        try
        {
            await using var content = file.OpenReadStream();
            var batch = await service.UploadAsync(
                all.Find(kind)!, access.Unit!, fileName, content, file.Length, CurrentCulture(), ct);
            var dto = await http.RequestServices.GetRequiredService<ImportQueries>().ToDtoAsync(batch, ct);
            return Results.Accepted(Routes.ImportBatch(kind, batch.Id), dto);
        }
        catch (Exception ex) when (ex is FileTypeNotAllowedException or FileTooLargeException)
        {
            return ApiProblems.BadRequest(ProblemCodes.import_file_invalid);
        }
    }

    private static async Task<IResult> ListAsync(ImportQueries queries, string kind, int? page, int? pageSize, CancellationToken ct) =>
        Results.Ok(await queries.ListAsync(kind, Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 20, 1, 200), ct));

    /// <summary>The batch of this kind, visible to the caller, with the permission checked at the batch's own unit.</summary>
    private static async Task<(ImportBatch? Batch, IResult? Problem)> LoadAsync(
        HttpContext http, ImportQueries queries, string kind, string permission, Guid id, CancellationToken ct)
    {
        var batch = await queries.FindAsync(kind, id, ct);
        if (batch is null)
        {
            return (null, ApiProblems.NotFound());
        }

        var access = await http.CheckUnitAsync(permission, batch.OrgUnitId, ct);
        return access.Problem is not null ? (null, access.Problem) : (batch, null);
    }

    private static async Task<IResult> GetAsync(
        HttpContext http, ImportQueries queries, string kind, string permission, Guid id, CancellationToken ct)
    {
        var (batch, problem) = await LoadAsync(http, queries, kind, permission, id, ct);
        return problem ?? Results.Ok(await queries.ToDtoAsync(batch!, ct));
    }

    private static async Task<IResult> RowsAsync(
        HttpContext http, ImportQueries queries, string kind, string permission, Guid id, bool errorsOnly, string? culture,
        int? page, int? pageSize, CancellationToken ct)
    {
        var (batch, problem) = await LoadAsync(http, queries, kind, permission, id, ct);
        return problem ?? Results.Ok(await queries.RowsAsync(
            batch!, errorsOnly, culture ?? batch!.Culture, Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 50, 1, 200), ct));
    }

    private static async Task<IResult> ErrorReportAsync(
        HttpContext http, ImportQueries queries, IFileStorage files, string kind, string permission, Guid id, CancellationToken ct)
    {
        var (batch, problem) = await LoadAsync(http, queries, kind, permission, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (batch!.ErrorReportFileId is not { } fileId)
        {
            return ApiProblems.NotFound();
        }

        var stream = await files.OpenReadAsync(fileId, ct);
        return Results.File(stream, FileContentTypes.ForExtension(".xlsx"), $"{kind}-errors-{id:N}.xlsx");
    }

    private static async Task<IResult> CommitAsync(
        HttpContext http, ImportQueries queries, ImportService service, string kind, string permission, Guid id, CancellationToken ct)
    {
        var (batch, problem) = await LoadAsync(http, queries, kind, permission, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        return await service.StartCommitAsync(kind, batch!.Id, ct) switch
        {
            CommitStart.Started => Results.Accepted(Routes.ImportBatch(kind, id), await queries.ToDtoAsync(
                (await queries.FindAsync(kind, id, ct))!, ct)),
            CommitStart.InProgress => ApiProblems.Conflict(ProblemCodes.import_in_progress),
            CommitStart.AlreadyCommitted => ApiProblems.Conflict(ProblemCodes.import_already_committed),
            CommitStart.NotValid => ApiProblems.Conflict(ProblemCodes.import_not_valid),
            _ => ApiProblems.NotFound(),
        };
    }

    private static async Task<IResult> DiscardAsync(
        HttpContext http, ImportQueries queries, ImportService service, string kind, string permission, Guid id, CancellationToken ct)
    {
        var (batch, problem) = await LoadAsync(http, queries, kind, permission, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        return await service.DiscardAsync(batch!, ct)
            ? Results.NoContent()
            : ApiProblems.Conflict(batch!.Status == ImportStatus.Committed ? ProblemCodes.import_already_committed : ProblemCodes.import_in_progress);
    }

    private static string CurrentCulture() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
}
