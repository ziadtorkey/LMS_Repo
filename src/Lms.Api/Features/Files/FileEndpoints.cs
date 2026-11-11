using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Files;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Microsoft.AspNetCore.Http.Features;

namespace Lms.Api.Features.Files;

public sealed record FileDto(Guid Id, string FileName, string ContentType, long SizeBytes, Guid OrgUnitId);

public sealed record FileUrlDto(string Url, DateTime ExpiresAtUtc);

/// <summary>File upload and presigned download. The upload size limit is the Files.MaxUploadMb setting, not Kestrel.</summary>
public static class FileEndpoints
{
    public const string Route = "/api/files";

    /// <summary>
    /// Coarse transport ceiling; the real, per-unit limit is applied after the file is read. Derived from the largest
    /// value the Files.MaxUploadMb setting accepts (plus 1 MB for the form around the file), so the setting can never
    /// promise more than this endpoint transports.
    /// </summary>
    public static readonly long TransportLimitBytes =
        (SettingCatalog.Find(SettingKeys.FilesMaxUploadMb.Name)!.Max!.Value + 1L) * 1024 * 1024;

    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(10);

    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        // Authentication (middleware) and the permission (endpoint filter) both run before the handler. The handler is
        // the only place the multipart body is read, so callers who are refused never make the server buffer a body.
        app.MapPost(Route, UploadAsync)
            .RequireAuthorization()
            .RequirePermission(Permissions.Files.Upload);

        app.MapGet(Route + "/{id:guid}/url", GetUrlAsync)
            .RequireAuthorization()
            .RequirePermission(Permissions.Files.Download);

        return app;
    }

    private static async Task<IResult> UploadAsync(HttpContext http, IFileStorage storage, CancellationToken cancellationToken)
    {
        var request = http.Request;
        if (!request.HasFormContentType)
        {
            return BadRequest("A multipart form with a file is required.");
        }

        // Kestrel's default body limit (about 30 MB) is far below the configurable Files.MaxUploadMb, which decides per
        // unit. Lift the transport limit here, before the body is touched; the cap is a coarse ceiling only.
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit)
        {
            bodyLimit.MaxRequestBodySize = TransportLimitBytes;
        }

        IFormCollection form;
        try
        {
            http.Features.Set<IFormFeature>(new FormFeature(request, new FormOptions
            {
                MultipartBodyLengthLimit = TransportLimitBytes,
            }));
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException or IOException)
        {
            return BadRequest("The multipart form is malformed or too large.");
        }

        var file = form.Files.GetFile("file");
        if (!Guid.TryParse(form["orgUnitId"], out var orgUnitId) || file is null || file.Length == 0)
        {
            return BadRequest("A non-empty file and a valid orgUnitId are required.");
        }

        var fileName = Path.GetFileName(file.FileName);
        if (fileName.Length > 255)
        {
            fileName = fileName[^255..];
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var stored = await storage.UploadAsync(
                new FileUploadRequest(fileName, file.ContentType, file.Length, orgUnitId), stream, cancellationToken);
            return Results.Created(
                $"{Route}/{stored.Id}",
                new FileDto(stored.Id, stored.FileName, stored.ContentType, stored.SizeBytes, stored.OrgUnitId));
        }
        catch (FileTooLargeException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "File too large",
                extensions: new Dictionary<string, object?> { ["maxBytes"] = ex.MaxBytes });
        }
        catch (FileTypeNotAllowedException)
        {
            return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "File type not allowed");
        }
        catch (UploadTargetNotFoundException)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found");
        }
    }

    private static async Task<IResult> GetUrlAsync(
        Guid id, IFileStorage storage, IClock clock, CancellationToken cancellationToken)
    {
        try
        {
            var url = await storage.GetDownloadUrlAsync(id, UrlLifetime, cancellationToken);
            return Results.Ok(new FileUrlDto(url.ToString(), clock.UtcNow.Add(UrlLifetime)));
        }
        catch (StoredFileNotFoundException)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found");
        }
    }

    private static IResult BadRequest(string title) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: title);
}
