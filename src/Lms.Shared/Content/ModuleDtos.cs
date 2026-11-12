namespace Lms.Shared.Content;

/// <summary>
/// A module node in the class lesson plan tree (nested in <c>ClassLessonDto.Modules</c>). For the Student role the
/// server sends only modules visible to the caller now (published, inside the schedule window, targeted at the whole
/// ClassCourse or at the caller's group) and fills <c>CompletionState</c>; staff get every module with
/// <c>IsVisibleNow</c> for the chips and <c>CompletionState = null</c>.
/// </summary>
public sealed record ModuleNodeDto(
    Guid Id,
    string Kind,
    int Order,
    string TitleAr,
    string TitleEn,
    string Visibility,
    DateTime? VisibleFromUtc,
    DateTime? VisibleToUtc,
    string Target,
    Guid? GroupId,
    string? GroupNameAr,
    string? GroupNameEn,
    string CompletionRule,
    bool IsVisibleNow,
    string? CompletionState);

/// <summary>
/// The typed content of a module. Content kinds (<see cref="ModuleKinds.Content"/>): File/Video/Audio carry
/// <c>Asset</c>; Page carries <c>BodyHtmlAr</c>/<c>BodyHtmlEn</c> as sanitised on write (D48, allow-list); Link carries
/// <c>Url</c> (http/https only). Other kinds carry <c>RefId</c> = the id of their assignment, quiz, library resource,
/// live session or package.
/// </summary>
public sealed record ModuleContentDto(
    AssetDto? Asset,
    string? BodyHtmlAr,
    string? BodyHtmlEn,
    string? Url,
    Guid? RefId);

/// <summary>
/// GET /api/modules/{id}. A student GET counts as a view (completes a <c>View</c>-rule module). <c>CanEdit</c> = the
/// caller may manage it (Modules.Manage and relationship). <c>ServerNowUtc</c> is the clock the schedule is judged by.
/// </summary>
public sealed record ModuleDetailDto(
    ModuleNodeDto Node,
    Guid ClassCourseId,
    Guid LessonKey,
    decimal? CompletionMinScore,
    ModuleContentDto Content,
    Guid? PreviousModuleId,
    Guid? NextModuleId,
    bool CanEdit,
    DateTime ServerNowUtc);

/// <summary>
/// Content of a content-kind module. File/Video/Audio: <c>AssetId</c> of an asset of the same ClassCourse whose kind
/// the module kind accepts (<see cref="AssetKinds.AcceptedBy"/>). Page: at least one of the bodies (HTML, max 200 000
/// characters each, sanitised by the server). Link: an absolute http/https <c>Url</c> (max 2 000).
/// </summary>
public sealed record ModuleContentInput(
    Guid? AssetId = null,
    string? BodyHtmlAr = null,
    string? BodyHtmlEn = null,
    string? Url = null);

/// <summary>
/// POST /api/class-courses/{id}/lessons/{lessonKey}/modules (appended last). Titles 1–200 in both languages.
/// <c>Scheduled</c> needs <c>VisibleFromUtc</c> and/or <c>VisibleToUtc</c>, from &lt; to (400
/// <c>module_visibility_range_invalid</c>); other visibilities need both null. <c>Group</c> needs a live group of the
/// ClassCourse, <c>ClassCourse</c> needs <c>GroupId = null</c> (400 <c>module_target_invalid</c>). Content kinds use
/// <see cref="CompletionRules.ForContent"/>; <c>CompletionMinScore</c> (0–100) only with <c>Score</c>.
/// </summary>
public sealed record CreateModuleRequest(
    string Kind,
    string TitleAr,
    string TitleEn,
    string Visibility,
    DateTime? VisibleFromUtc,
    DateTime? VisibleToUtc,
    string Target,
    Guid? GroupId,
    string CompletionRule,
    decimal? CompletionMinScore,
    ModuleContentInput Content);

/// <summary>PUT /api/modules/{id}. Same rules as <see cref="CreateModuleRequest"/>; the kind never changes.</summary>
public sealed record UpdateModuleRequest(
    string TitleAr,
    string TitleEn,
    string Visibility,
    DateTime? VisibleFromUtc,
    DateTime? VisibleToUtc,
    string Target,
    Guid? GroupId,
    string CompletionRule,
    decimal? CompletionMinScore,
    ModuleContentInput Content);

/// <summary>
/// PUT /api/class-courses/{id}/lessons/{lessonKey}/modules/order — the lesson's live module ids in the new order
/// (exactly the current set, else 400 <c>validation_failed</c>).
/// </summary>
public sealed record ReorderModulesRequest(IReadOnlyList<Guid> ModuleIds);

/// <summary>POST /api/modules/{id}/move — to another lesson of the same class lesson plan, appended last.</summary>
public sealed record MoveModuleRequest(Guid ToLessonKey);
