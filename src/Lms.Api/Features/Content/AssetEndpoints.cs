using Lms.Api.Features.Enrollment.Groups;
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
using Microsoft.EntityFrameworkCore;

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

    private static async Task<IResult> UploadAsync(
        HttpContext http, LmsDbContext db, IAssetService assets, PlacementGuard guard, CancellationToken ct)
    {
        var form = await UploadForm.ReadAsync(http, ct);
        if (form.Problem is not null)
        {
            return form.Problem;
        }

        var file = form.Form!.Files.GetFile(AssetUploadForm.File);
        if (!Guid.TryParse(form.Form["classCourseId"], out var classCourseId) || file is null || file.Length == 0)
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "A non-empty file and a valid classCourseId are required.");
        }

        var (classCourse, problem) = await http.CheckClassCourseAsync(Permissions.Modules.Manage, classCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await GroupEndpoints.WritableAsync(classCourse!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        var fileName = Path.GetFileName(file.FileName);
        if (fileName.Length > 255)
        {
            fileName = fileName[^255..];
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var asset = await assets.UploadAsync(classCourse!, fileName, file.ContentType, file.Length, stream, ct);
            return Results.Created(Routes.Asset(asset.Id), ToDto(asset));
        }
        catch (AssetTypeNotAllowedException)
        {
            return ApiProblems.BadRequest(ProblemCodes.asset_type_not_allowed);
        }
        catch (AssetTooLargeException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad request",
                extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.asset_too_large, ["maxBytes"] = ex.MaxBytes });
        }
    }

    private static async Task<IResult> LimitsAsync(
        Guid classCourseId, HttpContext http, IAssetService assets, CancellationToken ct)
    {
        var (classCourse, problem) = await http.CheckClassCourseAsync(Permissions.Modules.Manage, classCourseId, ct);
        return problem ?? Results.Ok(new AssetLimitsDto(await assets.GetMaxBytesAsync(classCourse!, ct), AssetTypes.All));
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

        var (_, problem) = await http.CheckClassCourseAsync(Permissions.Modules.Manage, asset.ClassCourseId, ct);
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
        AssetTypes.ForFileName(a.FileName)?.Previewable == true, a.CreatedAt);
}
