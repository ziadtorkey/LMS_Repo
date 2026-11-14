namespace Lms.Domain.Timetable;

/// <summary>Queues attendance-session generation after a timetable is published (implemented by Task 16; the default queues nothing).</summary>
public interface IAttendanceGenerationQueue
{
    /// <summary>True when generation was queued for the class and term.</summary>
    Task<bool> QueueAsync(Guid organizationId, Guid classId, Guid termId, CancellationToken cancellationToken);
}
