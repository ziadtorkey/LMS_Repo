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
}
