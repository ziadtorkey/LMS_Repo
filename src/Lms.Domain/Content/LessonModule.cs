using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Content;

/// <summary>
/// A teacher's module under one lesson of a class course (FR-LP-05, FR-CNT-01..02). The lesson is referenced by its stable
/// <see cref="LessonKey"/> (so a blueprint push never touches it) and by the class lesson row. Unit fields are the class
/// course's. Written only by the content module (M4a Task 3).
/// </summary>
[Audited]
public sealed class LessonModule : UnitOwnedEntity
{
    // The same values as Lms.Shared.Content (the domain does not reference the contract project; a unit test pins them).
    public const string PublishedVisibility = "Published";
    public const string ScheduledVisibility = "Scheduled";
    public const string ClassCourseTarget = "ClassCourse";
    public const string NoCompletion = "None";

    private LessonModule()
    {
        Kind = TitleAr = TitleEn = Visibility = Target = CompletionRule = string.Empty;
    }

    public LessonModule(ClassCourse classCourse, ClassLesson lesson, string kind, int order)
    {
        PlaceLike(classCourse);
        ClassCourseId = classCourse.Id;
        ClassLessonId = lesson.Id;
        LessonKey = lesson.Key;
        Kind = kind;
        Order = order;
        Visibility = PublishedVisibility;
        Target = ClassCourseTarget;
        CompletionRule = NoCompletion;
        TitleAr = TitleEn = string.Empty;
    }

    public Guid ClassCourseId { get; private set; }
    public Guid ClassLessonId { get; private set; }
    public Guid LessonKey { get; private set; }
    public string Kind { get; private set; }
    public int Order { get; private set; }
    public string TitleAr { get; private set; }
    public string TitleEn { get; private set; }
    public string Visibility { get; private set; }
    public DateTime? VisibleFromUtc { get; private set; }
    public DateTime? VisibleToUtc { get; private set; }
    public string Target { get; private set; }
    public Guid? GroupId { get; private set; }
    public string CompletionRule { get; private set; }
    public decimal? CompletionMinScore { get; private set; }

    /// <summary>The assignment, quiz, library resource, live session or package of the other kinds; null for content kinds.</summary>
    public Guid? RefId { get; private set; }

    public void Apply(
        string titleAr, string titleEn, string visibility, DateTime? visibleFromUtc, DateTime? visibleToUtc, string target,
        Guid? groupId, string completionRule, decimal? completionMinScore)
    {
        TitleAr = titleAr;
        TitleEn = titleEn;
        Visibility = visibility;
        VisibleFromUtc = visibleFromUtc;
        VisibleToUtc = visibleToUtc;
        Target = target;
        GroupId = groupId;
        CompletionRule = completionRule;
        CompletionMinScore = completionMinScore;
    }

    public void SetOrder(int order) => Order = order;

    public void MoveTo(ClassLesson lesson, int order)
    {
        ClassLessonId = lesson.Id;
        LessonKey = lesson.Key;
        Order = order;
    }

    public void SetRef(Guid? refId) => RefId = refId;

    /// <summary>Visible to a student's eyes now by its own settings (the lesson's hidden flag and the target are checked apart).</summary>
    public bool IsVisibleNow(DateTime nowUtc) => Visibility switch
    {
        PublishedVisibility => true,
        ScheduledVisibility => (VisibleFromUtc is null || VisibleFromUtc <= nowUtc) && (VisibleToUtc is null || nowUtc < VisibleToUtc),
        _ => false,
    };
}
