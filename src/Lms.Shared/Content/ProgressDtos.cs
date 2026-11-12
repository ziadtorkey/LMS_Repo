namespace Lms.Shared.Content;

/// <summary>Completed over total of the modules that count (rule not <c>None</c>, visible to the student now).</summary>
public sealed record ProgressDto(int Completed, int Total);

/// <summary>
/// GET /api/modules/{id}/completion — the caller's (student) state of one module. PUT on the same route marks a
/// <c>Manual</c>-rule module complete (idempotent; 409 <c>completion_not_manual</c> for other rules) and returns it.
/// </summary>
public sealed record ModuleCompletionDto(
    Guid ModuleId,
    string Rule,
    string State,
    DateTime? CompletedAtUtc,
    decimal? Score);

public sealed record LessonProgressDto(Guid LessonKey, ProgressDto Progress);

/// <summary>
/// GET /api/class-courses/{id}/progress[?studentId=…] — a student reads their own (no <c>studentId</c>); a teacher of
/// the ClassCourse or another staff caller with Modules.View passes an enrolled <c>studentId</c>. Rollups are computed
/// on read. <c>NextModuleId</c> = the first visible, counted, not completed module in plan order ("continue").
/// </summary>
public sealed record CourseProgressDto(
    Guid ClassCourseId,
    Guid StudentId,
    ProgressDto Course,
    IReadOnlyList<LessonProgressDto> Lessons,
    IReadOnlyList<ModuleCompletionDto> Modules,
    Guid? NextModuleId,
    DateTime ServerNowUtc);
