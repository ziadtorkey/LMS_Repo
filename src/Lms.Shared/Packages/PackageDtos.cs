using Lms.Shared.Content;

namespace Lms.Shared.Packages;

/// <summary>
/// A package upload (FR-STD-01/02). POST /api/class-courses/{id}/packages (multipart, see <see cref="PackageUploadForm"/>: the
/// archive, the lesson it is meant for and its kind; <c>Packages.Upload</c> on the ClassCourse, flag <c>ScormContent</c>, a
/// configured content host) answers 202 with <c>Uploaded</c>; validation runs as a system job. <c>LessonKey</c> anchors the package
/// in the lesson plan from the upload on, so the module list can show a "Checking…" row before the module exists; <c>ModuleId</c> is
/// set once it is attached. <c>Status</c> moves <c>Uploaded → Validating → Ready</c> or <c>Rejected</c> (<c>RejectReason</c> is a
/// <see cref="PackageRejectReasons"/> value, null otherwise). <c>Title</c>, <c>Version</c> (SCORM only) and <c>MainLibrary</c>
/// (H5P only) are read from the package once validated.
/// </summary>
public sealed record PackageUploadDto(
    Guid Id,
    Guid ClassCourseId,
    Guid LessonKey,
    string FileName,
    string Kind,
    string Status,
    string? RejectReason,
    string? Title,
    string? Version,
    string? MainLibrary,
    Guid? ModuleId,
    DateTime CreatedAtUtc);

/// <summary>Multipart field names of POST /api/class-courses/{id}/packages.</summary>
public static class PackageUploadForm
{
    /// <summary>The archive (.zip for SCORM, .h5p for H5P).</summary>
    public const string File = "file";

    /// <summary>The lesson (<c>LessonKey</c>) the package is for; required, it must be a lesson of the ClassCourse's plan.</summary>
    public const string LessonKey = "lessonKey";

    /// <summary>A <see cref="PackageKinds"/> value; the validation job refuses a package that is not of that kind.</summary>
    public const string Kind = "kind";
}

/// <summary>
/// GET /api/class-courses/{id}/packages — the packages of a ClassCourse that are not attached yet, for the authoring module list
/// ("Checking…", "Rejected: reason", "Ready: attach"). Permission <c>Modules.Manage</c> and the ClassCourse check (a Teacher of another
/// ClassCourse gets 404). Query: <c>lessonKey</c> (optional; one lesson only) and <c>status</c> (optional; a
/// <see cref="PackageUploadStatuses"/> value; default: every status except attached ones). Newest first; a package with a module
/// (<c>ModuleId</c> set) is not listed.
/// </summary>
public sealed record PackageListQuery(Guid? LessonKey = null, string? Status = null);

/// <summary>
/// POST /api/packages/{id}/module — attaches a <c>Ready</c> package to the lesson it was uploaded for (<c>LessonKey</c> of the upload) as a <c>Scorm</c> or
/// <c>H5p</c> module (<c>Packages.Upload</c> and <c>Modules.Manage</c> on the ClassCourse). Completion rule from
/// <see cref="CompletionRules.ForActivity"/> (<c>None</c>, <c>Submit</c>, <c>Score</c>). 409 <c>package_not_ready</c> unless
/// <c>Ready</c>. Returns the new module node.
/// </summary>
public sealed record AttachPackageModuleRequest(ModuleSettingsInput Module);

/// <summary>
/// POST /api/packages/{id}/launch (<c>Packages.View</c>, the caller is a student enrolled in the ClassCourse and the module is
/// visible to them; staff get 404 and use preview). <c>LaunchUrl</c> is absolute, on the content origin, with a signed session
/// token in its path. Calling it again re-checks access and issues a fresh token (reload, near <c>TokenExpiresAtUtc</c>).
/// <c>Resumed</c> = an open attempt continues. <c>ServerNowUtc</c> lets the page work with the server clock.
/// </summary>
public sealed record PackageLaunchDto(
    string LaunchUrl,
    Guid AttemptId,
    DateTime TokenExpiresAtUtc,
    DateTime ServerNowUtc,
    bool Resumed);

/// <summary>
/// POST /api/packages/{id}/preview — staff only (<c>Packages.View</c> and the staff relationship: a Teacher of the ClassCourse, other
/// staff by unit scope; a student gets 403). Like launch, but records no attempt, completion or grade: commits made while
/// previewing are accepted by the page and discarded by the server.
/// </summary>
public sealed record PackagePreviewDto(string LaunchUrl, DateTime TokenExpiresAtUtc, DateTime ServerNowUtc);

/// <summary>
/// POST /api/package-attempts/{id}/commit (<c>Packages.View</c>, the attempt's own student, attempt <c>InProgress</c>, module still
/// visible; preview tokens are refused). The page sends what the content reported over the private port: <c>Seq</c> rises by one
/// per message (the server keeps the highest and ignores lower ones: parallel requests arrive out of order), <c>Format</c> is a
/// <see cref="PackageCommitFormats"/> value and <c>Data</c> a flat key → value map limited to <see cref="ScormCmiKeys"/> and
/// <see cref="PackageTrackingLimits"/>. 204 on success; 409 <c>package_commit_rejected</c> for a closed attempt, 400 for an unknown
/// key or an oversize message; a faster commit than <c>Packages.CommitMinIntervalSeconds</c> is 429.
/// </summary>
public sealed record PackageCommitRequest(long Seq, string Format, IReadOnlyDictionary<string, string> Data);

/// <summary>
/// POST /api/package-attempts/{id}/finish — flushes and closes the attempt: <c>LastSeq</c> is the last commit the page sent (a barrier:
/// the server waits for the commits up to it). Returns the attempt as the server judged it; completion and the grade event follow
/// from it. 409 <c>package_commit_rejected</c> when already closed.
/// </summary>
public sealed record PackageFinishRequest(long LastSeq);

/// <summary>An attempt as the server judged it (<c>Completed</c> = the module's completion rule is met).</summary>
public sealed record PackageAttemptDto(
    Guid Id,
    string Status,
    bool Completed,
    decimal? ScoreRaw,
    decimal? ScoreMin,
    decimal? ScoreMax,
    long LastSeq,
    DateTime? CompletedAtUtc);
