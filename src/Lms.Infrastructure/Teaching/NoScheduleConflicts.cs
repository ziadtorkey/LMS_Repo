using Lms.Domain.Teaching;

namespace Lms.Infrastructure.Teaching;

/// <summary>No timetable exists before M5, so nothing can clash.</summary>
public sealed class NoScheduleConflicts : ITeacherScheduleConflicts
{
    public Task<IReadOnlyList<string>> FindAsync(Guid classCourseId, Guid userId, DateOnly from, DateOnly? to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
