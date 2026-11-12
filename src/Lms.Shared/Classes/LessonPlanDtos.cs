using Lms.Shared.Content;

namespace Lms.Shared.Classes;

/// <summary>
/// A lesson of a class lesson plan (a locked copy of the blueprint, FR-LP-03). <c>Key</c> = the blueprint lesson key;
/// <c>IsHidden</c> = removed from the blueprint (kept for history, never deleted). M4a: <c>Modules</c> = the teacher's
/// modules of this lesson in order (filtered for students, see <see cref="ModuleNodeDto"/>); <c>Progress</c> = the
/// calling student's progress in this lesson (null for staff). Both null from servers before M4a.
/// </summary>
public sealed record ClassLessonDto(
    Guid Key,
    int Order,
    string TitleAr,
    string TitleEn,
    string? ObjectivesAr,
    string? ObjectivesEn,
    DateOnly? PlannedDate,
    bool IsHidden,
    IReadOnlyList<ModuleNodeDto>? Modules = null,
    ProgressDto? Progress = null);

public sealed record ClassSectionDto(
    Guid Key,
    int Order,
    string TitleAr,
    string TitleEn,
    bool IsHidden,
    IReadOnlyList<ClassLessonDto> Lessons);

/// <summary>
/// GET /api/class-courses/{id}/lesson-plan. <c>BlueprintVersion</c> null = no published version received yet;
/// <c>SectionLabel</c> = the effective <c>Blueprints.SectionLabel</c>. M4a: <c>Progress</c> = the calling student's course
/// progress (null for staff); <c>ServerNowUtc</c> = the clock module schedules are judged by.
/// </summary>
public sealed record ClassLessonPlanDto(
    Guid ClassCourseId,
    int? BlueprintVersion,
    DateTime? SyncedAt,
    string SectionLabel,
    IReadOnlyList<ClassSectionDto> Sections,
    ProgressDto? Progress = null,
    DateTime? ServerNowUtc = null);
