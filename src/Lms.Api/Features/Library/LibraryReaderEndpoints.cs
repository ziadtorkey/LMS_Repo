using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Files;
using Lms.Domain.Library;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Content;

namespace Lms.Api.Features.Library;

/// <summary>
/// The protected reader and the download of a resource's file (D13). The reader always streams the file through the API,
/// inline, with no signed URL, so a resource with <c>DownloadAllowed</c> false can be read but never saved from a link. The
/// download route hands out a short-lived signed URL, and is 403 <c>download_disabled</c> when downloads are off.
/// </summary>
internal static class LibraryReaderEndpoints
{
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(10);

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.LibraryResourceReaderPattern, ReaderAsync).RequireAuthorization().RequirePermission(Permissions.Library.View);
        app.MapGet(Routes.LibraryResourceDownloadPattern, DownloadAsync).RequireAuthorization().RequirePermission(Permissions.Library.View);
    }

    private static async Task<IResult> ReaderAsync(
        Guid id, HttpContext http, LibraryQueries queries, IFileStorage storage, CancellationToken ct)
    {
        var (resource, asset, problem) = await FileOfAsync(id, http, queries, ct);
        if (problem is not null)
        {
            return problem;
        }

        _ = resource;
        var stream = await storage.OpenReadAsync(asset!.FileId, ct);
        http.Response.Headers.ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(asset.FileName)}";
        http.Response.Headers.CacheControl = "private, no-store";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.Stream(stream, asset.ContentType, enableRangeProcessing: stream.CanSeek);
    }

    private static async Task<IResult> DownloadAsync(
        Guid id, HttpContext http, LibraryQueries queries, IFileStorage storage, IClock clock, CancellationToken ct)
    {
        var (resource, asset, problem) = await FileOfAsync(id, http, queries, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (!resource!.DownloadAllowed)
        {
            return LibrarySupport.DownloadDisabled();
        }

        var url = await storage.GetDownloadUrlAsync(asset!.FileId, UrlLifetime, ct);
        return Results.Ok(new AssetUrlDto(url.ToString(), clock.UtcNow.Add(UrlLifetime), clock.UtcNow));
    }

    /// <summary>The resource and its file; a link, a resource without a file or one the caller cannot read is 404.</summary>
    private static async Task<(LibraryResource? Resource, Lms.Domain.Content.Asset? Asset, IResult? Problem)> FileOfAsync(
        Guid id, HttpContext http, LibraryQueries queries, CancellationToken ct)
    {
        var (resource, unit, problem) = await LibraryEndpoints.FindVisibleAsync(id, http, queries, ct);
        if (problem is not null)
        {
            return (null, null, problem);
        }

        if (resource!.FileAssetId is not { } fileId)
        {
            return (null, null, ApiProblems.NotFound());
        }

        var asset = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            queries.LibraryAssets(unit!.OrganizationId), a => a.Id == fileId, ct);
        return asset is null ? (null, null, ApiProblems.NotFound()) : (resource, asset, null);
    }
}
