using Lms.Shared.Timetable;

namespace Lms.Client.Services.Timetable;

/// <summary>Periods and the weekly timetable of a class (M5 wave 2, FR-TT-01..04). Conflicts are the server's answer; the client never predicts them.</summary>
public interface ITimetableApi
{
    Task<IReadOnlyList<PeriodDto>> GetPeriodsAsync(Guid unitId, CancellationToken ct = default);

    Task<PeriodDto> CreatePeriodAsync(Guid unitId, SavePeriodRequest request, CancellationToken ct = default);

    Task<PeriodDto> UpdatePeriodAsync(Guid unitId, Guid periodId, SavePeriodRequest request, CancellationToken ct = default);

    /// <summary>A period a slot uses cannot be deleted (409 <c>period_in_use</c>).</summary>
    Task DeletePeriodAsync(Guid unitId, Guid periodId, CancellationToken ct = default);

    Task<ClassTimetableDto> GetClassTimetableAsync(Guid classId, Guid termId, CancellationToken ct = default);

    /// <summary>In Block mode a clash is 409 <c>timetable_conflict</c> (<see cref="Api.ApiProblemException.Conflicts"/>); in Warn mode it saves and returns the warnings.</summary>
    Task<SaveSlotResultDto> CreateSlotAsync(Guid classId, SaveSlotRequest request, CancellationToken ct = default);

    /// <summary>A stale <c>Version</c> is 409 <c>concurrency_conflict</c>.</summary>
    Task<SaveSlotResultDto> UpdateSlotAsync(Guid classId, Guid slotId, SaveSlotRequest request, CancellationToken ct = default);

    Task DeleteSlotAsync(Guid classId, Guid slotId, CancellationToken ct = default);

    Task<TimetableValidationDto> ValidateAsync(Guid classId, Guid termId, CancellationToken ct = default);

    Task<PublishTimetableResultDto> PublishAsync(Guid classId, PublishTimetableRequest request, CancellationToken ct = default);

    Task<CopyTermResultDto> CopyTermAsync(Guid classId, CopyTermRequest request, CancellationToken ct = default);

    Task<(Stream Content, string FileName)> ExportAsync(Guid classId, Guid termId, CancellationToken ct = default);

    /// <summary>The dated changes of a lesson (a substitute, a cancellation, a move) without touching the base lesson (FR-TT-05).</summary>
    Task<IReadOnlyList<TimetableExceptionDto>> GetExceptionsAsync(Guid slotId, CancellationToken ct = default);

    /// <summary>A date off the lesson's weekday or outside the term is 400 <c>exception_date_invalid</c>; an unusable substitute 400 <c>exception_substitute_invalid</c>.</summary>
    Task<TimetableExceptionDto> CreateExceptionAsync(Guid slotId, SaveTimetableExceptionRequest request, CancellationToken ct = default);

    Task DeleteExceptionAsync(Guid slotId, Guid exceptionId, CancellationToken ct = default);

    /// <summary>The signed-in teacher's own lessons (substitutions included) or a student's own class, published only, at most 31 days.</summary>
    Task<TimetableViewDto> GetMyTimetableAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>A child's timetable for a guardian (an active link; another child is 404).</summary>
    Task<TimetableViewDto> GetStudentTimetableAsync(Guid studentId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
