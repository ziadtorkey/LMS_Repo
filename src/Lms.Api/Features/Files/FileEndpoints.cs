using Lms.Api.Infrastructure;
using Lms.Domain.Files;
using Lms.Shared.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lms.Api.Features.Files;

public sealed record FileDto(Guid Id, string FileName, string ContentType, long SizeBytes, Guid OrgUnitId);

public sealed record FileUrlDto(string Url, DateTime ExpiresAtUtc);

/// <summary>File upload and presigned download. The upload size limit is the Files.MaxUploadMb setting, not Kestrel.</summary>
public static class FileEndpoints
{
    public const string Route = "/api/files";

    /// <summary>Upper bound for the request body itself; the real, per-unit limit is applied after the file is read.</summary>
    private const long TransportLimitBytes = 1024L * 1024 * 1024;

    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(Route, UploadAsync)
            .RequirePermission(Permissions.Files.Upload)
            // Bearer-token API without cookies: antiforgery does not apply to it.
            .DisableAntiforgery()
            // Kestrel's default body limit (about 30 MB) is far below the configurable Files.MaxUploadMb; the setting
            // decides per unit, so the transport limit is only a coarse ceiling.
            .WithMetadata(new RequestSizeLimitAttribute(TransportLimitBytes))
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = TransportLimitBytes });

        app.MapGet(Route + "/{id:guid}/url", GetUrlAsync)
            .RequirePermission(Permissions.Files.Download);

        return app;
    }

    private static async Task<IResult> UploadAsync(
        IFileStorage storage, [FromForm] Guid orgUnitId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "A non-empty file is required.");
        }

        var fileName = Path.GetFileName(file.FileName);
        if (fileName.Length > 255)
        {
            fileName = fileName[^255..];
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
            var stored = await storage.UploadAsync(
                new FileUploadRequest(fileName, contentType, file.Length, orgUnitId), stream, cancellationToken);
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

    private static async Task<IResult> GetUrlAsync(Guid id, IFileStorage storage, CancellationToken cancellationToken)
    {
        try
        {
            var ttl = TimeSpan.FromMinutes(10);
            var url = await storage.GetDownloadUrlAsync(id, ttl, cancellationToken);
            return Results.Ok(new FileUrlDto(url.ToString(), DateTime.UtcNow.Add(ttl)));
        }
        catch (StoredFileNotFoundException)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found");
        }
    }
}
