using System.Text;
using Lms.Api.Features.Enrollment.Groups;
using Lms.Api.Features.Files;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Lms.Api.Features.Content;

/// <summary>
/// Asset upload, limits, metadata and signed URL (M4a Task 4, FR-CNT-03). The upload is a stream through the API; the size
/// limit is the unit's <c>Files.MaxUploadMb</c>. A read needs <c>Files.Download</c> and then either management of the owning
/// ClassCourse or an <see cref="IAssetReferenceReader"/> that says the caller can see something that uses the asset;
/// anything else is 404, so an asset id never leaks existence.
/// </summary>
public static class AssetEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.Assets, UploadAsync).RequireAuthorization().RequirePermission(Permissions.Files.Upload);
        app.MapGet(Routes.AssetsLimits, LimitsAsync).RequireAuthorization().RequirePermission(Permissions.Files.Upload);
        app.MapGet(Routes.AssetPattern, GetAsync).RequireAuthorization().RequirePermission(Permissions.Files.Download);
        app.MapGet(Routes.AssetUrlPattern, UrlAsync).RequireAuthorization().RequirePermission(Permissions.Files.Download);
    }

    /// <summary>
    /// Reads the multipart body as a stream: the owner part (<c>classCourseId</c>, or <c>orgUnitId</c> for a library asset) must
    /// come first, so the owner is checked (404 / permission / closed class) before the file part is read; the file part is then
    /// copied to a temp file, stopping at the effective size limit, and handed to the asset service.
    /// </summary>
    private static async Task<IResult> UploadAsync(
        HttpContext http, LmsDbContext db, IAssetService assets, PlacementGuard guard, CancellationToken ct)
    {
        if (!MediaTypeHeaderValue.TryParse(http.Request.ContentType, out var mediaType)
            || !string.Equals(mediaType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value is not { Length: > 0 } boundary)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "A multipart form with a file is required.");
        }

        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit)
        {
            bodyLimit.MaxRequestBodySize = FileEndpoints.TransportLimitBytes;
        }

        var reader = new MultipartReader(boundary, http.Request.Body) { BodyLengthLimit = FileEndpoints.TransportLimitBytes };
        string? tempPath = null;
        try
        {
            var first = await ReadSectionAsync(reader, ct);
            if (first is null || first.Name is not (AssetUploadForm.ClassCourseId or AssetUploadForm.OrgUnitId)
                || !Guid.TryParse(await ReadTextAsync(first, ct), out var ownerId))
            {
                return ApiProblems.BadRequest(
                    ProblemCodes.validation_failed, "classCourseId (or orgUnitId for a library asset) must be the first part of the form.");
            }

            Func<string, string, long, Stream, Task<Asset>> store;
            long maxBytes;
            if (first.Name == AssetUploadForm.ClassCourseId)
            {
                var (classCourse, problem) = await http.CheckClassCourseAsync(Permissions.Modules.Manage, ownerId, ct);
                if (problem is not null)
                {
                    return problem;
                }

                if (await GroupEndpoints.WritableAsync(classCourse!, db, guard, ct) is { } closed)
                {
                    return closed;
                }

                maxBytes = await assets.GetMaxBytesAsync(classCourse!, ct);
                store = (name, type, size, stream) => assets.UploadAsync(classCourse!, name, type, size, stream, ct);
            }
            else
            {
                // A library asset is owned by a unit: Library.Manage at that unit (an invisible unit is 404).
                var (unit, problem) = await http.CheckUnitAsync(Permissions.Library.Manage, ownerId, ct);
                if (problem is not null)
                {
                    return problem;
                }

                maxBytes = await assets.GetMaxBytesAsync(unit!, ct);
                store = (name, type, size, stream) => assets.UploadAsync(unit!, name, type, size, stream, ct);
            }

            var second = await ReadSectionAsync(reader, ct);
            if (second is null || second.Name != AssetUploadForm.File || string.IsNullOrEmpty(second.FileName))
            {
                return ApiProblems.BadRequest(ProblemCodes.validation_failed, "A file part is required after the owner part.");
            }

            var fileName = Path.GetFileName(second.FileName);
            if (fileName.Length > 255)
            {
                fileName = fileName[^255..];
            }

            if (AssetTypes.ForFileName(fileName) is null)
            {
                return ApiProblems.BadRequest(ProblemCodes.asset_type_not_allowed);
            }

            tempPath = Path.GetTempFileName();
            long length;
            await using (var temp = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                length = await CopyLimitedAsync(second.Section.Body, temp, maxBytes, ct);
            }

            if (length > maxBytes)
            {
                return TooLarge(maxBytes);
            }

            if (length == 0)
            {
                return ApiProblems.BadRequest(ProblemCodes.validation_failed, "The file is empty.");
            }

            await using var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            var asset = await store(fileName, second.ContentType, length, stream);
            return Results.Created(Routes.Asset(asset.Id), ToDto(asset));
        }
        catch (AssetTypeNotAllowedException)
        {
            return ApiProblems.BadRequest(ProblemCodes.asset_type_not_allowed);
        }
        catch (AssetTooLargeException ex)
        {
            return TooLarge(ex.MaxBytes);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException or IOException)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "The multipart form is malformed or too large.");
        }
        finally
        {
            if (tempPath is not null)
            {
                File.Delete(tempPath);
            }
        }
    }

    private sealed record Part(MultipartSection Section, string Name, string? FileName, string ContentType);

    private static async Task<Part?> ReadSectionAsync(MultipartReader reader, CancellationToken ct)
    {
        var section = await reader.ReadNextSectionAsync(ct);
        if (section is null || !ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
        {
            return null;
        }

        return new Part(
            section, HeaderUtilities.RemoveQuotes(disposition.Name).Value ?? string.Empty,
            HeaderUtilities.RemoveQuotes(disposition.FileName).Value, section.ContentType ?? string.Empty);
    }

    private static async Task<string> ReadTextAsync(Part part, CancellationToken ct)
    {
        using var text = new StreamReader(part.Section.Body, Encoding.UTF8, leaveOpen: true);
        return (await text.ReadToEndAsync(ct)).Trim();
    }

    /// <summary>Copies up to <paramref name="max"/> bytes; returns max + 1 as soon as the source has more.</summary>
    private static async Task<long> CopyLimitedAsync(Stream source, Stream target, long max, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > max)
            {
                return max + 1;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return total;
    }

    private static IResult TooLarge(long maxBytes) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Bad request",
        extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.asset_too_large, ["maxBytes"] = maxBytes });

    private static async Task<IResult> LimitsAsync(
        Guid? classCourseId, Guid? orgUnitId, HttpContext http, IAssetService assets, CancellationToken ct)
    {
        if ((classCourseId is null) == (orgUnitId is null))
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "Exactly one of classCourseId and orgUnitId is required.");
        }

        if (classCourseId is { } courseId)
        {
            var (classCourse, problem) = await http.CheckClassCourseAsync(Permissions.Modules.Manage, courseId, ct);
            return problem ?? Results.Ok(new AssetLimitsDto(await assets.GetMaxBytesAsync(classCourse!, ct), AssetTypes.All));
        }

        var (unit, unitProblem) = await http.CheckUnitAsync(Permissions.Library.Manage, orgUnitId!.Value, ct);
        return unitProblem ?? Results.Ok(new AssetLimitsDto(await assets.GetMaxBytesAsync(unit!, ct), AssetTypes.All));
    }

    private static async Task<IResult> GetAsync(
        Guid id, HttpContext http, LmsDbContext db, IEnumerable<IAssetReferenceReader> readers, CancellationToken ct)
    {
        var asset = await ReadableAsync(id, http, db, readers, ct);
        return asset is null ? ApiProblems.NotFound() : Results.Ok(ToDto(asset));
    }

    private static async Task<IResult> UrlAsync(
        Guid id, HttpContext http, LmsDbContext db, IEnumerable<IAssetReferenceReader> readers, IAssetService assets,
        IClock clock, CancellationToken ct)
    {
        var asset = await ReadableAsync(id, http, db, readers, ct);
        if (asset is null)
        {
            return ApiProblems.NotFound();
        }

        var url = await assets.GetUrlAsync(asset, ct);
        return Results.Ok(new AssetUrlDto(url.Url.ToString(), url.ExpiresAtUtc, clock.UtcNow));
    }

    /// <summary>The asset when the caller manages its ClassCourse or a registered reader allows it; null otherwise.</summary>
    private static async Task<Asset?> ReadableAsync(
        Guid id, HttpContext http, LmsDbContext db, IEnumerable<IAssetReferenceReader> readers, CancellationToken ct)
    {
        var asset = await db.Set<Asset>().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null)
        {
            return null;
        }

        // Manager of the owner: Modules.Manage on the class course, or Library.Manage at the owning unit of a library asset.
        var problem = asset.ClassCourseId is { } courseId
            ? (await http.CheckClassCourseAsync(Permissions.Modules.Manage, courseId, ct)).Problem
            : (await http.CheckUnitAsync(Permissions.Library.Manage, asset.OrgUnitId, ct)).Problem;
        if (problem is null)
        {
            return asset;
        }

        foreach (var reader in readers)
        {
            if (await reader.CanReadAsync(asset, ct))
            {
                return asset;
            }
        }

        return null;
    }

    private static AssetDto ToDto(Asset a) => new(
        a.Id, a.ClassCourseId, a.Kind, a.FileName, a.ContentType, a.SizeBytes, a.DurationSeconds,
        AssetTypes.ForFileName(a.FileName)?.Previewable == true, a.CreatedAt, a.ClassCourseId is null ? a.OrgUnitId : null);
}
