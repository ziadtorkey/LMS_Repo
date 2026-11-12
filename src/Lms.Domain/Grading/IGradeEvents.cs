namespace Lms.Domain.Grading;

public sealed record GradeEventInput(
    GradeSourceKind SourceKind,
    Guid SourceId,
    Guid ClassCourseId,
    Guid StudentProfileId,
    decimal Points,
    decimal MaxPoints,
    DateTime ScoredAtUtc,
    int Revision);

/// <summary>Records grade events for the M5 gradebook. There is no read API in M4a: M5 reads the table.</summary>
public interface IGradeEvents
{
    /// <summary>
    /// Adds the event to the current unit of work; the caller's <c>SaveChangesAsync</c> commits it with the grade itself. Returns false
    /// (and adds nothing) when that revision is already recorded for the source and student, so a retry is harmless.
    /// </summary>
    Task<bool> RecordAsync(GradeEventInput input, CancellationToken ct);

    /// <summary>
    /// The quiz path (no return step): called when an attempt is finalised or graded, with the student's score by the quiz grading
    /// method. Writes revision = latest + 1 (1 for the first), unless the latest event already carries the same points and max, so
    /// finalising an attempt that does not change the score writes nothing. Returns whether an event was added.
    /// </summary>
    Task<bool> RecordScoreAsync(
        GradeSourceKind sourceKind, Guid sourceId, Guid classCourseId, Guid studentProfileId, decimal points, decimal maxPoints,
        DateTime scoredAtUtc, CancellationToken ct);
}
