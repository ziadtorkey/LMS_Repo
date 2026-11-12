namespace Lms.Shared.Content;

/// <summary>What an uploaded asset is (FR-CNT-03); decides the viewer and which module kinds may use it.</summary>
public static class AssetKinds
{
    public const string Document = "Document";
    public const string Image = "Image";
    public const string Audio = "Audio";
    public const string Video = "Video";

    public static IReadOnlyList<string> All { get; } = [Document, Image, Audio, Video];

    /// <summary>
    /// The asset kinds a content module kind accepts: File takes documents and images, Video only video, Audio only
    /// audio; Page and Link take no asset (empty list).
    /// </summary>
    public static IReadOnlyList<string> AcceptedBy(string moduleKind) => moduleKind switch
    {
        ModuleKinds.File => [Document, Image],
        ModuleKinds.Video => [Video],
        ModuleKinds.Audio => [Audio],
        _ => [],
    };
}

/// <summary>One allowed file type. <c>Extension</c> is lower case with the dot; <c>Previewable</c> = shown in the browser.</summary>
public sealed record AssetTypeDto(string Extension, string ContentType, string Kind, bool Previewable);

/// <summary>
/// The v1 allow-list of asset file types (FR-CNT-03, X-05). The server checks the extension and the declared content
/// type against this list (400 <c>asset_type_not_allowed</c>). SVG and HTML are deliberately absent (script content).
/// </summary>
public static class AssetTypes
{
    public static IReadOnlyList<AssetTypeDto> All { get; } =
    [
        new(".pdf", "application/pdf", AssetKinds.Document, true),
        new(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", AssetKinds.Document, false),
        new(".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", AssetKinds.Document, false),
        new(".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", AssetKinds.Document, false),
        new(".txt", "text/plain", AssetKinds.Document, false),
        new(".png", "image/png", AssetKinds.Image, true),
        new(".jpg", "image/jpeg", AssetKinds.Image, true),
        new(".jpeg", "image/jpeg", AssetKinds.Image, true),
        new(".gif", "image/gif", AssetKinds.Image, true),
        new(".webp", "image/webp", AssetKinds.Image, true),
        new(".mp3", "audio/mpeg", AssetKinds.Audio, true),
        new(".m4a", "audio/mp4", AssetKinds.Audio, true),
        new(".ogg", "audio/ogg", AssetKinds.Audio, true),
        new(".wav", "audio/wav", AssetKinds.Audio, true),
        new(".mp4", "video/mp4", AssetKinds.Video, true),
        new(".webm", "video/webm", AssetKinds.Video, true),
    ];

    /// <summary>The allowed type for a file name (case-insensitive extension), or null.</summary>
    public static AssetTypeDto? ForFileName(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        if (dot < 0)
        {
            return null;
        }

        var extension = fileName[dot..];
        return All.FirstOrDefault(t => string.Equals(t.Extension, extension, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Multipart form field names of <c>POST /api/assets</c>.</summary>
public static class AssetUploadForm
{
    /// <summary>The file part.</summary>
    public const string File = "file";

    /// <summary>The owning ClassCourse id (the caller must reach it; the asset inherits its unit fields).</summary>
    public const string ClassCourseId = "classCourseId";

    /// <summary>
    /// M4a wave 2: the owning unit of a library asset (cover or file of a library resource), sent instead of
    /// <see cref="ClassCourseId"/> (exactly one of the two). Needs <c>Library.Manage</c> at that unit.
    /// </summary>
    public const string OrgUnitId = "orgUnitId";
}

/// <summary>
/// An uploaded asset. <c>DurationSeconds</c> is filled for audio and video when the server can read it. Exactly one
/// owner is set: <c>ClassCourseId</c> (modules, assignments, submissions, forum posts) or, since wave 2,
/// <c>OrgUnitId</c> (library assets owned by a unit).
/// </summary>
public sealed record AssetDto(
    Guid Id,
    Guid? ClassCourseId,
    string Kind,
    string FileName,
    string ContentType,
    long SizeBytes,
    int? DurationSeconds,
    bool Previewable,
    DateTime CreatedAtUtc,
    Guid? OrgUnitId = null);

/// <summary>
/// GET /api/assets/limits?classCourseId=… (or ?orgUnitId=… for library assets) — what the uploader validates before
/// sending. <c>MaxBytes</c> = the effective <c>Files.MaxUploadMb</c> at the ClassCourse's unit (or that unit).
/// </summary>
public sealed record AssetLimitsDto(long MaxBytes, IReadOnlyList<AssetTypeDto> AllowedTypes);

/// <summary>
/// A short-lived signed URL (GET /api/assets/{id}/url). Video and audio support HTTP range requests (no transcoding,
/// D51). The client refreshes once when <c>ExpiresAtUtc</c> has passed, judged against <c>ServerNowUtc</c>.
/// </summary>
public sealed record AssetUrlDto(string Url, DateTime ExpiresAtUtc, DateTime ServerNowUtc);
