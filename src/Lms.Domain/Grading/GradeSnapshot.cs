using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Grading;

/// <summary>
/// What a student was shown at a publish (Kind <c>Publish</c>, one row per student per batch) or what a lock froze (Kind <c>Lock</c>, M7; L5). The
/// values are kept as a JSON document so a later regrade or a deleted item never rewrites what was published; the document is
/// <c>[AuditIgnore]</c>, so no grade value ever reaches the audit log. Append-only: rows are never updated.
/// </summary>
[Audited]
public sealed class GradeSnapshot : UnitOwnedEntity
{
    public const string PublishKind = "Publish";
    public const string LockKind = "Lock";

    private GradeSnapshot()
    {
        Kind = ValuesJson = string.Empty;
    }

    public GradeSnapshot(Gradebook gradebook, Guid studentProfileId, string kind, Guid batchId, int itemCount, string valuesJson, Guid? byUserId, DateTime nowUtc)
    {
        PlaceLike(gradebook);
        GradebookId = gradebook.Id;
        StudentProfileId = studentProfileId;
        Kind = kind;
        BatchId = batchId;
        ItemCount = itemCount;
        ValuesJson = valuesJson;
        ByUserId = byUserId;
        AtUtc = nowUtc;
    }

    public Guid GradebookId { get; private set; }
    public Guid StudentProfileId { get; private set; }
    public string Kind { get; private set; }

    /// <summary>The publish batch: every snapshot of one publish call shares it.</summary>
    public Guid BatchId { get; private set; }

    /// <summary>How many items the batch published for this student.</summary>
    public int ItemCount { get; private set; }

    [AuditIgnore]
    public string ValuesJson { get; private set; }

    public Guid? ByUserId { get; private set; }
    public DateTime AtUtc { get; private set; }
}
