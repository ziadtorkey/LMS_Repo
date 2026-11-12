using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Content;

/// <summary>
/// An uploaded file used by lessons (FR-CNT-03): the bytes and size limit live in the file store (<see cref="FileId"/>),
/// this row adds what it is (kind, original name) and which ClassCourse owns it. Unit fields are copied from the
/// ClassCourse, so the asset follows its visibility and is re-pathed with it.
/// </summary>
[Audited]
public sealed class Asset : UnitOwnedEntity
{
    private Asset()
    {
        Kind = FileName = ContentType = string.Empty;
    }

    public Asset(ClassCourse classCourse, Guid fileId, string kind, string fileName, string contentType, long sizeBytes)
    {
        PlaceLike(classCourse);
        ClassCourseId = classCourse.Id;
        FileId = fileId;
        Kind = kind;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
    }

    public Guid ClassCourseId { get; private set; }

    /// <summary>The stored object (<c>StoredFile</c>).</summary>
    public Guid FileId { get; private set; }

    /// <summary>One of <c>AssetKinds.All</c>.</summary>
    public string Kind { get; private set; }

    public string FileName { get; private set; }
    public string ContentType { get; private set; }
    public long SizeBytes { get; private set; }

    /// <summary>Audio and video length in seconds when the server could read it.</summary>
    public int? DurationSeconds { get; private set; }

    public void SetDuration(int? seconds) => DurationSeconds = seconds;
}
