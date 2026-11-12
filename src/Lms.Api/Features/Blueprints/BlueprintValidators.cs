using Lms.Shared;
using Lms.Shared.Blueprints;

namespace Lms.Api.Features.Blueprints;

/// <summary>One section of a draft tree after validation: keys are assigned, order is the list position.</summary>
internal sealed record TreeSection(Guid Key, string TitleAr, string TitleEn, IReadOnlyList<TreeLesson> Lessons);

internal sealed record TreeLesson(
    Guid Key, string TitleAr, string TitleEn, string? ObjectivesAr, string? ObjectivesEn, int? PlannedOffsetDays);

/// <summary>Field rules of the blueprint bodies (M3 plan Task 16): sizes, titles, offsets and key ownership.</summary>
internal static class BlueprintValidators
{
    public const int MaxSections = 100;
    public const int MaxLessons = 500;
    public const int MaxTitle = 200;
    public const int MaxObjectives = 4000;
    public const int MaxOffsetDays = 400;

    public static IResult Invalid(string field, string key) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { [field] = [key] },
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });

    public static IResult TooMany(string field, int limit) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest, title: "Bad request", detail: $"At most {limit} items are allowed.",
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.too_many_items, ["field"] = field, ["limit"] = limit });

    public static IResult Problem(int status, string title, string code, string? detail = null) =>
        Results.Problem(statusCode: status, title: title, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>
    /// Validates the tree of a draft save. A key is valid when it is unique in the request and already belongs to this
    /// course's history (<paramref name="knownSectionKeys"/> / <paramref name="knownLessonKeys"/>), so a client cannot
    /// smuggle in keys of another course; null keys get new ones.
    /// </summary>
    public static (List<TreeSection>? Tree, IResult? Problem) Validate(
        IReadOnlyList<BlueprintSectionInput>? sections, IReadOnlySet<Guid> knownSectionKeys, IReadOnlySet<Guid> knownLessonKeys)
    {
        if (sections is null)
        {
            return (null, Invalid("Sections", "required"));
        }

        if (sections.Count > MaxSections)
        {
            return (null, TooMany("Sections", MaxSections));
        }

        if (sections.Sum(s => s?.Lessons?.Count ?? 0) > MaxLessons)
        {
            return (null, TooMany("Sections", MaxLessons));
        }

        var sectionKeys = new HashSet<Guid>();
        var lessonKeys = new HashSet<Guid>();
        var tree = new List<TreeSection>();
        foreach (var section in sections)
        {
            if (section is null || !Title(section.TitleAr) || !Title(section.TitleEn))
            {
                return (null, Invalid("Sections", "title_invalid"));
            }

            var key = section.Key ?? Guid.NewGuid();
            if (section.Key is not null && (!knownSectionKeys.Contains(key) || !sectionKeys.Add(key)))
            {
                return (null, Invalid("Sections", "key_invalid"));
            }

            sectionKeys.Add(key);
            var lessons = new List<TreeLesson>();
            foreach (var lesson in section.Lessons ?? [])
            {
                if (lesson is null || !Title(lesson.TitleAr) || !Title(lesson.TitleEn))
                {
                    return (null, Invalid("Lessons", "title_invalid"));
                }

                if (lesson.ObjectivesAr?.Length > MaxObjectives || lesson.ObjectivesEn?.Length > MaxObjectives)
                {
                    return (null, Invalid("Lessons", "objectives_too_long"));
                }

                if (lesson.PlannedOffsetDays is < 0 or > MaxOffsetDays)
                {
                    return (null, Invalid("Lessons", "offset_invalid"));
                }

                var lessonKey = lesson.Key ?? Guid.NewGuid();
                if (lesson.Key is not null && (!knownLessonKeys.Contains(lessonKey) || !lessonKeys.Add(lessonKey)))
                {
                    return (null, Invalid("Lessons", "key_invalid"));
                }

                lessonKeys.Add(lessonKey);
                lessons.Add(new TreeLesson(
                    lessonKey, lesson.TitleAr.Trim(), lesson.TitleEn.Trim(), Clean(lesson.ObjectivesAr), Clean(lesson.ObjectivesEn),
                    lesson.PlannedOffsetDays));
            }

            tree.Add(new TreeSection(key, section.TitleAr.Trim(), section.TitleEn.Trim(), lessons));
        }

        return (tree, null);
    }

    private static bool Title(string? value) => value is not null && value.Trim().Length is >= 1 and <= MaxTitle;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
