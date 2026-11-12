namespace Lms.Domain.Teaching;

/// <summary>
/// Reports timetable clashes of a teacher assignment (FR-TCH-03 warning, deferred to M5). Until then the registered
/// implementation finds none; assigning a teacher already calls it so M5 only replaces the implementation.
/// </summary>
public interface ITeacherScheduleConflicts
{
    /// <summary>Human-readable conflicts of teaching the class course from the date (empty when none).</summary>
    Task<IReadOnlyList<string>> FindAsync(Guid classCourseId, Guid userId, DateOnly from, DateOnly? to, CancellationToken cancellationToken);
}
