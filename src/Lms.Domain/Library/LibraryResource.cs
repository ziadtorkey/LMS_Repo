using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Organizations;

namespace Lms.Domain.Library;

/// <summary>
/// A digital library resource (FR-LIB-01, D13). Owned by a unit: visible there and below, read-only below the owner. The file
/// and the cover are unit-owned assets; a <c>Link</c> carries a URL instead. Edits and deletes are audited.
/// </summary>
[Audited]
public sealed class LibraryResource : UnitOwnedEntity
{
    private LibraryResource()
    {
        TitleAr = TitleEn = Type = string.Empty;
    }

    public LibraryResource(
        OrgUnit owner, string titleAr, string titleEn, string? descriptionAr, string? descriptionEn, string type, Guid? courseId,
        Guid? coverAssetId, Guid? fileAssetId, string? url, bool downloadAllowed)
    {
        PlaceIn(owner);
        TitleAr = titleAr.Trim();
        TitleEn = titleEn.Trim();
        DescriptionAr = Clean(descriptionAr);
        DescriptionEn = Clean(descriptionEn);
        Type = type;
        CourseId = courseId;
        CoverAssetId = coverAssetId;
        FileAssetId = fileAssetId;
        Url = url;
        DownloadAllowed = downloadAllowed;
    }

    public string TitleAr { get; private set; }
    public string TitleEn { get; private set; }
    public string? DescriptionAr { get; private set; }
    public string? DescriptionEn { get; private set; }

    /// <summary>One of <c>LibraryResourceTypes.All</c>.</summary>
    public string Type { get; private set; }

    /// <summary>The subject: a catalog course (optional).</summary>
    public Guid? CourseId { get; private set; }

    public Guid? CoverAssetId { get; private set; }
    public Guid? FileAssetId { get; private set; }

    /// <summary>Set for the <c>Link</c> type only.</summary>
    public string? Url { get; private set; }

    /// <summary>False: the file opens only in the protected reader, never as a download (D13).</summary>
    public bool DownloadAllowed { get; private set; }

    public void Update(
        string titleAr, string titleEn, string? descriptionAr, string? descriptionEn, string type, Guid? courseId, Guid? coverAssetId,
        Guid? fileAssetId, string? url, bool downloadAllowed)
    {
        TitleAr = titleAr.Trim();
        TitleEn = titleEn.Trim();
        DescriptionAr = Clean(descriptionAr);
        DescriptionEn = Clean(descriptionEn);
        Type = type;
        CourseId = courseId;
        CoverAssetId = coverAssetId;
        FileAssetId = fileAssetId;
        Url = url;
        DownloadAllowed = downloadAllowed;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>A tag of a resource (whole-word filter). Same unit as the resource.</summary>
public sealed class LibraryTag : UnitOwnedEntity
{
    private LibraryTag()
    {
        Tag = string.Empty;
    }

    public LibraryTag(LibraryResource resource, string tag)
    {
        PlaceLike(resource);
        ResourceId = resource.Id;
        Tag = tag;
    }

    public Guid ResourceId { get; private set; }
    public string Tag { get; private set; }
}

/// <summary>A level type a resource is meant for; a resource without any is for every level.</summary>
public sealed class LibraryResourceLevel : UnitOwnedEntity
{
    private LibraryResourceLevel()
    {
    }

    public LibraryResourceLevel(LibraryResource resource, Guid levelTypeId)
    {
        PlaceLike(resource);
        ResourceId = resource.Id;
        LevelTypeId = levelTypeId;
    }

    public Guid ResourceId { get; private set; }
    public Guid LevelTypeId { get; private set; }
}
