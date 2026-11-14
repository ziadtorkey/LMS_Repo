namespace Lms.Domain.Timetable;

/// <summary>Whether a timetable slot uses a period; a used period cannot be deleted (409 <c>period_in_use</c>). Task 14 replaces the default.</summary>
public interface IPeriodUsage
{
    Task<bool> IsUsedAsync(Guid periodId, CancellationToken cancellationToken);
}
