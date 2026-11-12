using Lms.Domain.Content;
using Lms.Domain.Courses;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.Library;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Library;

/// <summary>Validation of a library resource (FR-LIB-01) and small shared helpers.</summary>
internal static class LibrarySupport
{
    public const int MaxTitle = 200, MaxDescription = 2000, MaxUrl = 2000, MaxTags = 20, MaxTag = 50;

    public static IResult Invalid(string field, string reason = "invalid") =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { [field] = [reason] },
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });

    public static IResult DownloadDisabled() =>
        Results.Problem(
            statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.download_disabled });

    /// <summary>The normalised tags (trimmed, distinct ignoring case) or null when invalid.</summary>
    public static List<string>? CleanTags(IReadOnlyList<string>? tags)
    {
        var clean = (tags ?? []).Select(t => (t ?? string.Empty).Trim()).ToList();
        if (clean.Any(t => t.Length is 0 or > MaxTag))
        {
            return null;
        }

        var distinct = clean.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count > MaxTags ? null : distinct;
    }

    /// <summary>The first problem of a save request, checked in order; null when valid. Absolute http/https only for a link.</summary>
    public static async Task<IResult?> ValidateAsync(
        SaveLibraryResourceRequest request, OrgUnit unit, LmsDbContext db, ICourseCatalog catalog, CancellationToken ct)
    {
        if (Bad(request.TitleAr, MaxTitle) || Bad(request.TitleEn, MaxTitle))
        {
            return Invalid("title", "length");
        }

        if ((request.DescriptionAr?.Length ?? 0) > MaxDescription || (request.DescriptionEn?.Length ?? 0) > MaxDescription)
        {
            return Invalid("description", "length");
        }

        if (!LibraryResourceTypes.All.Contains(request.Type))
        {
            return Invalid("type", "invalid_value");
        }

        if (CleanTags(request.Tags) is null)
        {
            return Invalid("tags", "invalid");
        }

        if (request.Type == LibraryResourceTypes.Link)
        {
            if (request.FileAssetId is not null || !IsAbsoluteWebUrl(request.Url))
            {
                return Invalid("url", "invalid");
            }
        }
        else if (request.Url is not null || request.FileAssetId is null)
        {
            return Invalid("fileAssetId", "required");
        }

        var levelIds = (request.LevelTypeIds ?? []).Distinct().ToList();
        if (levelIds.Count > 0 && await db.LevelTypes.CountAsync(l => levelIds.Contains(l.Id), ct) != levelIds.Count)
        {
            return Invalid("levelTypeIds", "not_found");
        }

        // The subject is a catalog course available at (or above) the resource's unit.
        if (request.CourseId is { } courseId && !await catalog.IsAvailableAtAsync(courseId, unit, ct))
        {
            return Invalid("courseId", "not_found");
        }

        if (request.FileAssetId is { } fileId)
        {
            var file = await db.Set<Asset>().FirstOrDefaultAsync(a => a.Id == fileId && a.OrgUnitId == unit.Id && a.ClassCourseId == null, ct);
            if (file is null || !FileKindFits(request.Type, file.Kind))
            {
                return Invalid("fileAssetId", "not_found");
            }
        }

        if (request.CoverAssetId is { } coverId
            && !await db.Set<Asset>().AnyAsync(a => a.Id == coverId && a.OrgUnitId == unit.Id && a.ClassCourseId == null && a.Kind == AssetKinds.Image, ct))
        {
            return Invalid("coverAssetId", "not_found");
        }

        return null;
    }

    private static bool Bad(string? text, int max) => string.IsNullOrWhiteSpace(text) || text.Trim().Length > max;

    private static bool IsAbsoluteWebUrl(string? url) =>
        url is { Length: > 0 and <= MaxUrl } && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>Books and documents are document files; video and audio resources need that kind; Other takes any file.</summary>
    private static bool FileKindFits(string type, string assetKind) => type switch
    {
        LibraryResourceTypes.Book or LibraryResourceTypes.Document => assetKind == AssetKinds.Document,
        LibraryResourceTypes.Video => assetKind == AssetKinds.Video,
        LibraryResourceTypes.Audio => assetKind == AssetKinds.Audio,
        _ => true,
    };
}
