namespace Lms.Shared.Blueprints;

public static class BlueprintStatuses
{
    public const string Draft = "Draft";
    public const string Published = "Published";
    public const string Superseded = "Superseded";

    public static IReadOnlyList<string> All { get; } = [Draft, Published, Superseded];
}

/// <summary>How a published version reaches class lesson plans (FR-LP-04).</summary>
public static class BlueprintApplyModes
{
    /// <summary>Only classes created later (and plans that never received a version) get it.</summary>
    public const string NewClassesOnly = "NewClassesOnly";

    /// <summary>Existing class plans of open years are merged by key (background job).</summary>
    public const string PushToExisting = "PushToExisting";

    public static IReadOnlyList<string> All { get; } = [NewClassesOnly, PushToExisting];
}

/// <summary>
/// One version of a course blueprint. <c>Version</c> 0 = the draft; <c>Revision</c> guards concurrent draft saves.
/// <c>Status</c> from <see cref="BlueprintStatuses"/>; <c>ApplyMode</c> from <see cref="BlueprintApplyModes"/> once published.
/// </summary>
public sealed record BlueprintSummaryDto(
    Guid Id,
    Guid CourseId,
    int Version,
    string Status,
    int Revision,
    DateTime? PublishedAt,
    string? PublishedByNameAr,
    string? PublishedByNameEn,
    string? ApplyMode,
    int SectionCount,
    int LessonCount);

/// <summary>A lesson. <c>Key</c> is stable across versions; <c>PlannedOffsetDays</c> from the year start (0..400).</summary>
public sealed record BlueprintLessonDto(
    Guid Key,
    int Order,
    string TitleAr,
    string TitleEn,
    string? ObjectivesAr,
    string? ObjectivesEn,
    int? PlannedOffsetDays);

/// <summary>A section (week, unit or chapter per <c>Blueprints.SectionLabel</c>). <c>Key</c> is stable across versions.</summary>
public sealed record BlueprintSectionDto(
    Guid Key,
    int Order,
    string TitleAr,
    string TitleEn,
    IReadOnlyList<BlueprintLessonDto> Lessons);

public sealed record BlueprintDto(BlueprintSummaryDto Summary, IReadOnlyList<BlueprintSectionDto> Sections);

/// <summary>A lesson in a draft save; null <c>Key</c> = new lesson.</summary>
public sealed record BlueprintLessonInput(
    Guid? Key,
    string TitleAr,
    string TitleEn,
    string? ObjectivesAr,
    string? ObjectivesEn,
    int? PlannedOffsetDays);

/// <summary>A section in a draft save; null <c>Key</c> = new section; order = list position.</summary>
public sealed record BlueprintSectionInput(
    Guid? Key,
    string TitleAr,
    string TitleEn,
    IReadOnlyList<BlueprintLessonInput> Lessons);

/// <summary>Body of PUT /api/courses/{id}/blueprints/draft: the whole tree. A stale <c>Revision</c> → 409 <c>blueprint_stale</c>.</summary>
public sealed record SaveBlueprintDraftRequest(int Revision, IReadOnlyList<BlueprintSectionInput> Sections);

/// <summary>Body of POST /api/courses/{id}/blueprints/draft/publish.</summary>
public sealed record PublishBlueprintRequest(string ApplyMode);

/// <summary>Result of publish. <c>ClassesQueued</c> = class plans the push job will update.</summary>
public sealed record PublishBlueprintResultDto(BlueprintSummaryDto Blueprint, int ClassesQueued);

/// <summary>Body of POST /api/courses/{id}/blueprints/draft/copy-from. <c>SourceVersion</c> null = latest published.</summary>
public sealed record CopyBlueprintRequest(Guid SourceCourseId, int? SourceVersion);
