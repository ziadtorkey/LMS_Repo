namespace Lms.Shared.Grading;

/// <summary>
/// The configuration of a gradebook (<c>PUT …/gradebook/config</c>, <c>Grades.Manage</c>): the scale (one of the scales visible at the unit,
/// D58), the decimals, the category method and the missing-score policy. A gradebook keeps its own copy of the scale it was created with,
/// so editing the scale later does not change published grades.
/// </summary>
public sealed record GradebookConfigDto(
    Guid? ScaleId,
    string? ScaleNameAr,
    string? ScaleNameEn,
    bool ScaleProvisional,
    int DecimalPlaces,
    string CategoryMethod,
    string MissingPolicy,
    string RoundingMode);

public sealed record SaveGradebookConfigRequest(Guid? ScaleId, int DecimalPlaces, string CategoryMethod, string MissingPolicy);

/// <summary>A category with its weight (0-100). The built-in <c>Uncategorised</c> category (weight 0) holds new auto items (L3).</summary>
public sealed record GradeCategoryDto(Guid Id, string NameAr, string NameEn, decimal Weight, int Order, bool IsUncategorised);

/// <summary>One category of a save request; <c>Id</c> null creates it. Weights outside 0-100 are 400 <c>weights_invalid</c>; a total other than 100 is only a flag in the grid.</summary>
public sealed record SaveGradeCategoryRequest(Guid? Id, string NameAr, string NameEn, decimal Weight, int Order);

/// <summary>PUT <c>…/gradebook/categories</c> (<c>Grades.Manage</c>): the category list as a whole.</summary>
public sealed record SaveGradeCategoriesRequest(IReadOnlyList<SaveGradeCategoryRequest> Categories);

/// <summary>
/// A column of the grid. <c>SourceKind</c>/<c>SourceId</c> identify the grade event source of an auto item (assignment, quiz, package; an
/// unknown kind shows a generic title); a manual item has none. <c>PublishState</c> is a <see cref="PublishStates"/> value.
/// </summary>
public sealed record GradeItemDto(
    Guid Id,
    Guid CategoryId,
    string TitleAr,
    string TitleEn,
    string? SourceKind,
    Guid? SourceId,
    bool IsManual,
    decimal MaxPoints,
    string PublishState,
    DateTime? PublishAtUtc,
    DateTime? PublishedAtUtc,
    int Order);

/// <summary>POST <c>…/gradebook/items</c> (<c>Grades.Manage</c>): a manual item. Max points 0.01-1000.</summary>
public sealed record CreateGradeItemRequest(Guid CategoryId, string TitleAr, string TitleEn, decimal MaxPoints);

/// <summary>PUT <c>…/gradebook/items/{iid}</c>: move to a category, rename (manual items), change max points (manual items). DELETE only for manual items.</summary>
public sealed record UpdateGradeItemRequest(Guid CategoryId, string TitleAr, string TitleEn, decimal MaxPoints, int Order);

/// <summary>One cell of a student's row. <c>Points</c> null with <c>State = Empty</c> or <c>Excused</c>; <c>Reason</c> only for an override.</summary>
public sealed record GradeCellDto(Guid ItemId, decimal? Points, string State, string? Reason);

/// <summary>A category's result for one student, computed by the server (the client never computes, L18).</summary>
public sealed record GradeCategoryResultDto(Guid CategoryId, decimal? Percent, decimal? Weighted);

/// <summary>
/// A student's row. <c>Final</c> and <c>BandCode</c> are null when the missing-score policy hides the total or nothing is graded;
/// <c>Version</c> is the row version a write sends back (stale = 409 <c>concurrency_conflict</c>). A student who left the course mid-term stays in the
/// grid for staff with the stored grades, marked <c>IsWithdrawn</c> (<c>WithdrawnOn</c> = the day the enrollment ended): withdrawn students are
/// left out of class statistics and are never part of what other students or guardians see.
/// </summary>
public sealed record GradeRowDto(
    Guid StudentId,
    string StudentNameAr,
    string StudentNameEn,
    string StudentNumber,
    IReadOnlyList<GradeCellDto> Cells,
    IReadOnlyList<GradeCategoryResultDto> CategoryResults,
    decimal? FinalPercent,
    string? BandCode,
    string? BandLabelAr,
    string? BandLabelEn,
    decimal? BandPoints,
    long Version,
    bool IsWithdrawn = false,
    DateOnly? WithdrawnOn = null);

/// <summary>Query of <c>GET /api/class-courses/{id}/gradebook</c>. <c>TermId</c> null = the current term of the class (L17: 50 students per page, at most 100).</summary>
public sealed record GradebookQuery(Guid? TermId = null, int Page = 1, int PageSize = GradingLimits.GridPageSize);

/// <summary>
/// GET <c>/api/class-courses/{id}/gradebook?termId=&amp;page=</c> (<c>Grades.Manage</c> or <c>Grades.View</c>; Teacher own ClassCourses with an
/// assignment active today, Supervisor read only; a miss is 404). The staff view: every item and every cell, including hidden ones. A caller
/// with <c>Grades.Manage</c> also runs the idempotent backfill of events recorded before M5 (never in a closed year, L4).
/// <c>IsValid</c> = the weights are in range and total 100; <c>WeightsTotal</c> their sum. <c>EventsAfterLock</c> counts events ignored after a
/// lock (staff marker only, L5). Every number is computed by the server; <c>ComputedAtUtc</c> and <c>ServerNowUtc</c> let the page show how fresh it is.
/// </summary>
public sealed record GradebookDto(
    Guid Id,
    Guid ClassCourseId,
    Guid TermId,
    string TermNameAr,
    string TermNameEn,
    GradebookConfigDto Config,
    IReadOnlyList<GradeCategoryDto> Categories,
    IReadOnlyList<GradeItemDto> Items,
    PagedResult<GradeRowDto> Rows,
    bool IsValid,
    decimal WeightsTotal,
    string State,
    DateTime? LockedAtUtc,
    int EventsAfterLock,
    bool CanManage,
    bool CanPublish,
    DateTime ComputedAtUtc,
    DateTime ServerNowUtc);

/// <summary>One change to one cell (see <see cref="GradeEntryActions"/>). <c>Points</c> for <c>Set</c> and <c>Override</c> (0 to the item's max points, else 422 <c>grade_out_of_range</c>); <c>Reason</c> (1-500) required for <c>Override</c> (<c>grade_override_reason_required</c>).</summary>
public sealed record GradeEntryChange(Guid ItemId, string Action, decimal? Points, string? Reason);

/// <summary>
/// PUT <c>…/gradebook/entries</c> (<c>Grades.Manage</c>; <c>PlacementGuard</c>; 409 <c>gradebook_locked</c> when locked). The changes of one student,
/// applied together, with the <c>Version</c> of the row the page loaded: a stale version is 409 <c>concurrency_conflict</c> and the answer is the
/// current row. Event scores are read only. The answer is the recomputed <see cref="GradeRowDto"/>.
/// </summary>
public sealed record UpsertEntriesRequest(Guid StudentId, long Version, IReadOnlyList<GradeEntryChange> Changes);

/// <summary>
/// POST <c>…/gradebook/items/publish</c> (<c>Grades.Publish</c>; Teacher own ClassCourses). <c>Mode</c> from <see cref="PublishModes"/>;
/// <c>AtUtc</c> for <c>Schedule</c> must be after the server clock. Publishing under a provisional scale is 409 <c>scale_provisional</c> unless
/// <c>AcknowledgeProvisionalScale</c> is true (audited, L7).
/// </summary>
public sealed record PublishItemsRequest(IReadOnlyList<Guid> ItemIds, string Mode, DateTime? AtUtc, bool AcknowledgeProvisionalScale);

/// <summary>The effect of a publish: students whose visible grades changed and the notifications queued (one per student per batch, L26).</summary>
public sealed record PublishResultDto(int ItemsChanged, int AffectedStudents, int NotificationsQueued);

/// <summary>One row of a grade column preview.</summary>
public sealed record GradeImportRowDto(int Row, string StudentNumber, decimal? Points, bool Overwrites, string? ErrorCode);

/// <summary>
/// POST <c>…/gradebook/items/{iid}/import/preview</c> (multipart <c>.xlsx</c> only, <c>Grades.Manage</c>, L9): what applying would do.
/// <c>Token</c> is bound to (uploader, gradebook, item, file hash) and valid <see cref="GradingLimits.ImportTokenMinutes"/> minutes;
/// 400 <c>grade_import_invalid</c> for a wrong file type or limits exceeded.
/// </summary>
public sealed record GradeImportPreviewDto(
    string Token,
    DateTime ExpiresAtUtc,
    int TotalRows,
    int Overwrites,
    IReadOnlyList<string> UnknownCodes,
    IReadOnlyList<GradeImportRowDto> OutOfRange,
    IReadOnlyList<GradeImportRowDto> Errors,
    IReadOnlyList<GradeImportRowDto> Rows);

/// <summary>POST <c>…/import/apply</c>: re-validates against current data; an expired or foreign token is 400 <c>grade_import_expired</c>. Event-derived scores are never written.</summary>
public sealed record ApplyGradeImportRequest(string Token);

/// <summary>The result of an apply: rows written and rows skipped with their codes.</summary>
public sealed record GradeImportResultDto(int Written, IReadOnlyList<GradeImportRowDto> Skipped);
