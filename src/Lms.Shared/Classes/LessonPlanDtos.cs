namespace Lms.Shared.Classes;

/// <summary>
/// A lesson of a class lesson plan (a locked copy of the blueprint, FR-LP-03). <c>Key</c> = the blueprint lesson key;
/// <c>IsHidden</c> = removed from the blueprint (kept for history, never deleted).
/// </summary>
public sealed record ClassLessonDto(
    Guid Key,
    int Order,
    string TitleAr,
    string TitleEn,
    string? ObjectivesAr,
    string? ObjectivesEn,
    DateOnly? PlannedDate,
    bool IsHidden);

public sealed record ClassSectionDto(
    Guid Key,
    int Order,
    string TitleAr,
    string TitleEn,
    bool IsHidden,
    IReadOnlyList<ClassLessonDto> Lessons);

/// <summary>
/// GET /api/class-courses/{id}/lesson-plan. <c>BlueprintVersion</c> null = no published version received yet;
/// <c>SectionLabel</c> = the effective <c>Blueprints.SectionLabel</c>.
/// </summary>
public sealed record ClassLessonPlanDto(
    Guid ClassCourseId,
    int? BlueprintVersion,
    DateTime? SyncedAt,
    string SectionLabel,
    IReadOnlyList<ClassSectionDto> Sections);
