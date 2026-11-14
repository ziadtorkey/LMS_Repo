using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Timetable;

namespace Lms.Client.Services.Timetable;

public sealed class TimetableApi(ApiHttp http) : ITimetableApi
{
    public async Task<IReadOnlyList<PeriodDto>> GetPeriodsAsync(Guid unitId, CancellationToken ct = default) =>
        await http.GetAsync<List<PeriodDto>>(Routes.OrgUnitPeriods(unitId), ct);

    public async Task<PeriodDto> CreatePeriodAsync(Guid unitId, SavePeriodRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<PeriodDto>(HttpMethod.Post, Routes.OrgUnitPeriods(unitId), request, ct))!;

    public async Task<PeriodDto> UpdatePeriodAsync(Guid unitId, Guid periodId, SavePeriodRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<PeriodDto>(HttpMethod.Put, Routes.Period(unitId, periodId), request, ct))!;

    public Task DeletePeriodAsync(Guid unitId, Guid periodId, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.Period(unitId, periodId), null, ct);

    public Task<ClassTimetableDto> GetClassTimetableAsync(Guid classId, Guid termId, CancellationToken ct = default) =>
        http.GetAsync<ClassTimetableDto>(Routes.ClassTimetable(classId, termId), ct);

    public async Task<SaveSlotResultDto> CreateSlotAsync(Guid classId, SaveSlotRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<SaveSlotResultDto>(HttpMethod.Post, Routes.ClassTimetableSlots(classId), request, ct))!;

    public async Task<SaveSlotResultDto> UpdateSlotAsync(Guid classId, Guid slotId, SaveSlotRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<SaveSlotResultDto>(HttpMethod.Put, Routes.ClassTimetableSlot(classId, slotId), request, ct))!;

    public Task DeleteSlotAsync(Guid classId, Guid slotId, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.ClassTimetableSlot(classId, slotId), null, ct);

    public Task<TimetableValidationDto> ValidateAsync(Guid classId, Guid termId, CancellationToken ct = default) =>
        http.GetAsync<TimetableValidationDto>(Routes.ClassTimetableValidate(classId, termId), ct);

    public async Task<PublishTimetableResultDto> PublishAsync(Guid classId, PublishTimetableRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<PublishTimetableResultDto>(HttpMethod.Post, Routes.ClassTimetablePublish(classId), request, ct))!;

    public async Task<CopyTermResultDto> CopyTermAsync(Guid classId, CopyTermRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<CopyTermResultDto>(HttpMethod.Post, Routes.ClassTimetableCopyTerm(classId), request, ct))!;

    public Task<(Stream Content, string FileName)> ExportAsync(Guid classId, Guid termId, CancellationToken ct = default) =>
        http.DownloadAsync(Routes.ClassTimetableExport(classId, termId), ct);
}
