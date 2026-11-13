using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Quizzes;

/// <summary>
/// A quiz of a class course (FR-QZ-01, FR-QZ-02). It owns the questions, options and attempts; its lesson module (Kind
/// Quiz, RefId = this id) carries title, visibility, target and completion rule. Unit fields are the class course unit fields.
/// </summary>
[Audited]
public sealed class Quiz : UnitOwnedEntity, ILiveKeyed
{
    private Quiz()
    {
        GradingMethod = ReviewPolicy = string.Empty;
    }

    /// <summary>The quiz exists before its module (the module RefId needs the id): call <see cref="AttachModule"/> once it is created.</summary>
    public Quiz(ClassCourse classCourse, Guid lessonKey)
    {
        PlaceLike(classCourse);
        ClassCourseId = classCourse.Id;
        LessonKey = lessonKey;
        GradingMethod = ReviewPolicy = string.Empty;
    }

    public Guid ClassCourseId { get; private set; }
    public Guid LessonKey { get; private set; }
    public Guid ModuleId { get; private set; }

    /// <summary>The module id while live; makes "one quiz per module" unique.</summary>
    public Guid? ModuleKey { get; private set; }

    public string? DescriptionAr { get; private set; }
    public string? DescriptionEn { get; private set; }
    public int? TimeLimitMinutes { get; private set; }
    public int? MaxAttempts { get; private set; }
    public string GradingMethod { get; private set; }
    public bool ShuffleQuestions { get; private set; }
    public bool ShuffleOptions { get; private set; }
    public DateTime? OpensAtUtc { get; private set; }
    public DateTime? ClosesAtUtc { get; private set; }
    public string ReviewPolicy { get; private set; }
    public int? DrawCount { get; private set; }

    public void Apply(
        string? descriptionAr, string? descriptionEn, int? timeLimitMinutes, int? maxAttempts, string gradingMethod, bool shuffleQuestions,
        bool shuffleOptions, DateTime? opensAtUtc, DateTime? closesAtUtc, string reviewPolicy, int? drawCount)
    {
        DescriptionAr = descriptionAr;
        DescriptionEn = descriptionEn;
        TimeLimitMinutes = timeLimitMinutes;
        MaxAttempts = maxAttempts;
        GradingMethod = gradingMethod;
        ShuffleQuestions = shuffleQuestions;
        ShuffleOptions = shuffleOptions;
        OpensAtUtc = opensAtUtc;
        ClosesAtUtc = closesAtUtc;
        ReviewPolicy = reviewPolicy;
        DrawCount = drawCount;
    }

    public void AttachModule(Guid moduleId)
    {
        ModuleId = moduleId;
        ModuleKey = moduleId;
    }

    public void ClearLiveKeys() => ModuleKey = null;
}
