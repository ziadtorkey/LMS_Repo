using Lms.Domain.Auditing;
using Lms.Domain.Common;

namespace Lms.Domain.Grading;

/// <summary>
/// A column of the gradebook. An auto item belongs to a grade event source (<c>SourceKind</c> is the raw <see cref="GradeSourceKind"/> number,
/// so a kind this version does not know is kept and shown with a generic title); a manual item (paper test) has no source. Auto items start
/// <c>Hidden</c> (L3). Unique per (gradebook, source kind, source) among live rows.
/// </summary>
[Audited]
public sealed class GradeItem : UnitOwnedEntity, ILiveKeyed
{
    public const string HiddenState = "Hidden";
    public const string PublishedState = "Published";
    public const string ScheduledState = "Scheduled";

    private GradeItem()
    {
        TitleAr = TitleEn = PublishState = string.Empty;
    }

    private GradeItem(Gradebook gradebook, GradeCategory category, string titleAr, string titleEn, decimal maxPoints, int order)
    {
        PlaceLike(gradebook);
        GradebookId = gradebook.Id;
        CategoryId = category.Id;
        TitleAr = titleAr;
        TitleEn = titleEn;
        MaxPoints = maxPoints;
        Order = order;
        PublishState = HiddenState;
    }

    /// <summary>An item for a grade event source (hidden, in the given category).</summary>
    public static GradeItem ForSource(
        Gradebook gradebook, GradeCategory category, int sourceKind, Guid sourceId, string titleAr, string titleEn, decimal maxPoints, int order) =>
        new(gradebook, category, titleAr, titleEn, maxPoints, order) { SourceKind = sourceKind, SourceId = sourceId, SourceKey = sourceId };

    /// <summary>A manual item (no source), hidden until published.</summary>
    public static GradeItem Manual(Gradebook gradebook, GradeCategory category, string titleAr, string titleEn, decimal maxPoints, int order) =>
        new(gradebook, category, titleAr, titleEn, maxPoints, order);

    public Guid GradebookId { get; private set; }
    public Guid CategoryId { get; private set; }
    public string TitleAr { get; private set; }
    public string TitleEn { get; private set; }
    public int? SourceKind { get; private set; }
    public Guid? SourceId { get; private set; }

    /// <summary>The source id while live (the unique key with the gradebook and the kind); null for a manual item.</summary>
    public Guid? SourceKey { get; private set; }

    public decimal MaxPoints { get; private set; }
    public string PublishState { get; private set; }
    public DateTime? PublishAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public int Order { get; private set; }

    public bool IsManual => SourceKind is null;

    public void MoveTo(GradeCategory category, int order)
    {
        CategoryId = category.Id;
        Order = order;
    }

    public void Rename(string titleAr, string titleEn)
    {
        TitleAr = titleAr;
        TitleEn = titleEn;
    }

    public void SetMaxPoints(decimal maxPoints) => MaxPoints = maxPoints;

    public void Publish(DateTime nowUtc)
    {
        PublishState = PublishedState;
        PublishAtUtc = null;
        PublishedAtUtc ??= nowUtc;
    }

    public void Schedule(DateTime atUtc)
    {
        PublishState = ScheduledState;
        PublishAtUtc = atUtc;
    }

    public void Hide()
    {
        PublishState = HiddenState;
        PublishAtUtc = null;
    }

    public void ClearLiveKeys() => SourceKey = null;
}
