using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Packages;

/// <summary>
/// An uploaded SCORM or H5P archive (M4b, FR-STD-01/02). The archive lives in the file store (<see cref="ArchiveFileId"/>); the
/// extracted files, once the validation job has run, live in the package store under <see cref="StorePrefix"/>. Unit fields are
/// copied from the ClassCourse, so the package follows its visibility and is re-pathed with it. <see cref="LessonKey"/> anchors it
/// in the lesson plan from the upload on, so the module list can show a "Checking…" row before the module exists.
/// </summary>
[Audited]
public sealed class PackageUpload : UnitOwnedEntity
{
    public const string UploadedStatus = "Uploaded";
    public const string ValidatingStatus = "Validating";
    public const string ReadyStatus = "Ready";
    public const string RejectedStatus = "Rejected";

    private PackageUpload()
    {
        Kind = FileName = Status = Sha256 = StorePrefix = string.Empty;
    }

    public PackageUpload(
        ClassCourse classCourse, Guid lessonKey, string kind, string fileName, Guid archiveFileId, long sizeBytes, string sha256)
    {
        PlaceLike(classCourse);
        ClassCourseId = classCourse.Id;
        LessonKey = lessonKey;
        Kind = kind;
        FileName = fileName;
        ArchiveFileId = archiveFileId;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        Status = UploadedStatus;
        StorePrefix = string.Empty;
    }

    public Guid ClassCourseId { get; private set; }

    /// <summary>The lesson of the ClassCourse's plan the package is for.</summary>
    public Guid LessonKey { get; private set; }

    /// <summary>A <c>PackageKinds</c> value, chosen at upload and checked by the validation job.</summary>
    public string Kind { get; private set; }

    /// <summary>The name the teacher uploaded (display only; never a path).</summary>
    public string FileName { get; private set; }

    /// <summary>The archive in the file store.</summary>
    public Guid ArchiveFileId { get; private set; }

    public long SizeBytes { get; private set; }

    public string Sha256 { get; private set; }

    /// <summary>A <c>PackageUploadStatuses</c> value.</summary>
    public string Status { get; private set; }

    /// <summary>A <c>PackageRejectReasons</c> value while <see cref="Status"/> is <c>Rejected</c>.</summary>
    public string? RejectReason { get; private set; }

    public string? Title { get; private set; }

    /// <summary>SCORM only: a <c>ScormVersions</c> value.</summary>
    public string? Version { get; private set; }

    /// <summary>H5P only: the main library machine name.</summary>
    public string? MainLibrary { get; private set; }

    /// <summary>The prefix of the extracted files in the package store (<c>{orgId}/{id}/</c>), empty until extracted.</summary>
    public string StorePrefix { get; private set; }

    /// <summary>The real uncompressed size of the extracted files; the client's package storage quota adds these up.</summary>
    public long ExtractedBytes { get; private set; }

    /// <summary>The module that uses the package, set when it is attached to its lesson.</summary>
    public Guid? ModuleId { get; private set; }

    public void MarkValidating() => Status = ValidatingStatus;

    public void Reject(string reason)
    {
        Status = RejectedStatus;
        RejectReason = reason;
    }

    public void MarkReady(string title, string? version, string? mainLibrary, string storePrefix, long extractedBytes = 0)
    {
        ExtractedBytes = extractedBytes;
        Status = ReadyStatus;
        RejectReason = null;
        Title = title;
        Version = version;
        MainLibrary = mainLibrary;
        StorePrefix = storePrefix;
    }

    public void AttachTo(Guid moduleId) => ModuleId = moduleId;
}
