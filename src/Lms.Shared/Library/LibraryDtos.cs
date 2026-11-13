using Lms.Shared.Content;

namespace Lms.Shared.Library;

/// <summary>Library resource types (FR-LIB-01). <c>Link</c> carries a URL instead of a file.</summary>
public static class LibraryResourceTypes
{
    public const string Book = "Book";
    public const string Document = "Document";
    public const string Video = "Video";
    public const string Audio = "Audio";
    public const string Link = "Link";
    public const string Other = "Other";

    public static IReadOnlyList<string> All { get; } = [Book, Document, Video, Audio, Link, Other];
}

/// <summary>
/// A library resource (FR-LIB-01, D13). Owned by a unit (<c>OrgUnitId</c>) and visible there and below; below the
/// owner it is read-only (<c>IsInherited</c>, <c>CanEdit</c> false). Students see it only when one of
/// <c>LevelTypeIds</c> matches their unit's level type (empty = every level). <c>CourseId</c> = the subject, a catalog
/// course (optional). <c>File</c> and <c>Cover</c> are unit-owned assets; <c>Url</c> is set for <c>Link</c> only. With
/// <c>DownloadAllowed</c> false the file opens only in the protected reader.
/// </summary>
public sealed record LibraryResourceDto(
    Guid Id,
    Guid OrgUnitId,
    string OrgUnitNameAr,
    string OrgUnitNameEn,
    bool IsInherited,
    string TitleAr,
    string TitleEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string Type,
    IReadOnlyList<Guid> LevelTypeIds,
    Guid? CourseId,
    string? CourseNameAr,
    string? CourseNameEn,
    IReadOnlyList<string> Tags,
    AssetDto? Cover,
    AssetDto? File,
    string? Url,
    bool DownloadAllowed,
    bool CanEdit,
    DateTime CreatedAtUtc);

/// <summary>
/// Query of GET /api/library (paged, title order). <c>OrgUnitId</c> = the unit to browse (default: the caller's
/// context unit); results are the resources owned there or above. Filters compose (AND); <c>Tag</c> matches a whole tag.
/// </summary>
public sealed record LibraryQuery(
    Guid? OrgUnitId = null,
    string? Search = null,
    string? Type = null,
    Guid? LevelTypeId = null,
    Guid? CourseId = null,
    string? Tag = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>
/// POST /api/library (Library.Manage at <c>OrgUnitId</c>) and PUT /api/library/{id} (the owner never changes). Titles
/// 1–200 both languages; descriptions plain text, max 2 000; tags 0–20 distinct, 1–50 characters each; level types of
/// the client; <c>CourseId</c> a course available at the unit. <c>Link</c> needs an absolute http/https <c>Url</c>
/// (max 2 000) and no file; the other types need <c>FileAssetId</c> (an asset owned by that unit) and no URL.
/// <c>CoverAssetId</c> is an image asset of that unit.
/// </summary>
public sealed record SaveLibraryResourceRequest(
    Guid OrgUnitId,
    string TitleAr,
    string TitleEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string Type,
    IReadOnlyList<Guid> LevelTypeIds,
    Guid? CourseId,
    IReadOnlyList<string> Tags,
    Guid? CoverAssetId,
    Guid? FileAssetId,
    string? Url,
    bool DownloadAllowed);

/// <summary>
/// POST /api/library/{id}/add-to-lesson — adds the resource as a <c>LibraryResource</c> module (<c>RefId</c> = the
/// resource id) under <c>LessonKey</c> of the ClassCourse (Modules.Manage on the ClassCourse and Library.View of a
/// resource visible at the ClassCourse's unit). Completion rule from
/// <see cref="CompletionRules.ForActivity"/>. Returns the new module node.
/// </summary>
public sealed record AddLibraryResourceToLessonRequest(
    Guid ClassCourseId,
    Guid LessonKey,
    ModuleSettingsInput Module);
