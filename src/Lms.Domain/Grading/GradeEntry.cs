using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Grading;

/// <summary>
/// What a teacher typed: the points of a manual item, an override of an event score (with a reason, audited), or an excuse (the item does not
/// count for the student). Event scores themselves are never copied here: they are read from the grade events. One live row per (item,
/// student); <see cref="Version"/> is the optimistic concurrency token (L12): a write that carries an older version is refused.
/// </summary>
[Audited]
public sealed class GradeEntry : UnitOwnedEntity, ILiveKeyed
{
    private GradeEntry()
    {
    }

    public GradeEntry(GradeItem item, Guid studentProfileId, Guid? byUserId, DateTime nowUtc)
    {
        PlaceLike(item);
        GradebookId = item.GradebookId;
        GradeItemId = item.Id;
        ItemKey = item.Id;
        StudentProfileId = studentProfileId;
        ByUserId = byUserId;
        AtUtc = nowUtc;
    }

    public Guid GradebookId { get; private set; }
    public Guid GradeItemId { get; private set; }

    /// <summary>The item id while live (unique with the student).</summary>
    public Guid? ItemKey { get; private set; }

    public Guid StudentProfileId { get; private set; }
    public decimal? Points { get; private set; }
    public bool IsExcused { get; private set; }
    public bool IsOverride { get; private set; }
    public string? Reason { get; private set; }
    public Guid? ByUserId { get; private set; }
    public DateTime AtUtc { get; private set; }

    /// <summary>Row version: incremented by every change; also an EF concurrency token.</summary>
    public int Version { get; private set; }

    public void SetPoints(decimal? points, Guid? byUserId, DateTime nowUtc)
    {
        Points = points;
        IsOverride = false;
        IsExcused = false;
        Reason = null;
        Touch(byUserId, nowUtc);
    }

    public void SetOverride(decimal points, string reason, Guid? byUserId, DateTime nowUtc)
    {
        Points = points;
        IsOverride = true;
        IsExcused = false;
        Reason = reason;
        Touch(byUserId, nowUtc);
    }

    public void ClearOverride(Guid? byUserId, DateTime nowUtc)
    {
        Points = null;
        IsOverride = false;
        Reason = null;
        Touch(byUserId, nowUtc);
    }

    public void Excuse(Guid? byUserId, DateTime nowUtc)
    {
        IsExcused = true;
        Points = null;
        IsOverride = false;
        Reason = null;
        Touch(byUserId, nowUtc);
    }

    public void ClearExcuse(Guid? byUserId, DateTime nowUtc)
    {
        IsExcused = false;
        Touch(byUserId, nowUtc);
    }

    /// <summary>The teacher looked at a newer event score and keeps the override: it is dated now, so the "newer score" marker goes.</summary>
    public void Acknowledge(Guid? byUserId, DateTime nowUtc) => Touch(byUserId, nowUtc);

    private void Touch(Guid? byUserId, DateTime nowUtc)
    {
        ByUserId = byUserId;
        AtUtc = nowUtc;
        Version++;
    }

    public void ClearLiveKeys() => ItemKey = null;
}
