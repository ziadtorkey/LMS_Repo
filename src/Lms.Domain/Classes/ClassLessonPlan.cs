using Lms.Domain.Common;

namespace Lms.Domain.Classes;

/// <summary>
/// The lesson plan of one class course: a copy of a blueprint version that the class's teachers adapt. One live plan per
/// class course. Tables only in M3 Task 16 (not audited yet: AuditCatalog lists only Blueprint; Task 17 adds [Audited] together with the catalog entry); the logic (copy on create, merge on publish, edits) is Task 17.
/// </summary>
public sealed class ClassLessonPlan : UnitOwnedEntity, ILiveKeyed
{
    private ClassLessonPlan()
    {
    }

    public ClassLessonPlan(ClassCourse classCourse, Guid? blueprintId, int? blueprintVersion, DateTime? syncedAt)
    {
        PlaceLike(classCourse);
        ClassCourseId = classCourse.Id;
        ClassCourseKey = classCourse.Id;
        BlueprintId = blueprintId;
        BlueprintVersion = blueprintVersion;
        SyncedAt = syncedAt;
    }

    public Guid ClassCourseId { get; private set; }

    /// <summary>The class course id while live; makes "one plan per class course" unique.</summary>
    public Guid? ClassCourseKey { get; private set; }

    public Guid? BlueprintId { get; private set; }
    public int? BlueprintVersion { get; private set; }
    public DateTime? SyncedAt { get; private set; }

    public void SyncedTo(Guid blueprintId, int version, DateTime nowUtc)
    {
        BlueprintId = blueprintId;
        BlueprintVersion = version;
        SyncedAt = nowUtc;
    }

    public void ClearLiveKeys() => ClassCourseKey = null;
}
