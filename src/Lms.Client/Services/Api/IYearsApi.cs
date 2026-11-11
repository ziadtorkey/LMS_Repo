using Lms.Shared;
using Lms.Shared.AcademicYears;

namespace Lms.Client.Services.Api;

/// <summary>Academic years, terms and calendar (FR-YEAR-01..03).</summary>
public interface IYearsApi
{
    Task<YearContextDto> GetEffectiveAsync(Guid? orgUnitId, CancellationToken ct = default);
    Task<AcademicYearDto> CreateYearAsync(SaveAcademicYearRequest request, CancellationToken ct = default);
    Task<AcademicYearDto> UpdateYearAsync(Guid id, SaveAcademicYearRequest request, CancellationToken ct = default);
    Task ActivateYearAsync(Guid id, CancellationToken ct = default);
    Task DeleteYearAsync(Guid id, CancellationToken ct = default);
    Task<TermDto> CreateTermAsync(Guid yearId, SaveTermRequest request, CancellationToken ct = default);
    Task<TermDto> UpdateTermAsync(Guid id, SaveTermRequest request, CancellationToken ct = default);
    Task DeleteTermAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarEventDto>> GetCalendarAsync(Guid yearId, Guid? orgUnitId, CancellationToken ct = default);
    Task<CalendarEventDto> CreateEventAsync(SaveCalendarEventRequest request, CancellationToken ct = default);
    Task<CalendarEventDto> UpdateEventAsync(Guid id, SaveCalendarEventRequest request, CancellationToken ct = default);
    Task DeleteEventAsync(Guid id, CancellationToken ct = default);
}

public sealed class YearsApi(ApiHttp http) : IYearsApi
{
    public async Task<YearContextDto> GetEffectiveAsync(Guid? orgUnitId, CancellationToken ct = default) =>
        await http.GetAsync<YearContextDto>(
            orgUnitId is { } id ? $"{Routes.AcademicYearsEffective}?orgUnitId={id}" : Routes.AcademicYearsEffective, ct);

    public async Task<AcademicYearDto> CreateYearAsync(SaveAcademicYearRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<AcademicYearDto>(HttpMethod.Post, Routes.AcademicYears, request, ct))!;

    public async Task<AcademicYearDto> UpdateYearAsync(Guid id, SaveAcademicYearRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<AcademicYearDto>(HttpMethod.Put, Routes.AcademicYear(id), request, ct))!;

    public Task ActivateYearAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.AcademicYearActivate(id), null, ct);

    public Task DeleteYearAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.AcademicYear(id), null, ct);

    public async Task<TermDto> CreateTermAsync(Guid yearId, SaveTermRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<TermDto>(HttpMethod.Post, Routes.AcademicYearTerms(yearId), request, ct))!;

    public async Task<TermDto> UpdateTermAsync(Guid id, SaveTermRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<TermDto>(HttpMethod.Put, Routes.Term(id), request, ct))!;

    public Task DeleteTermAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.Term(id), null, ct);

    public async Task<IReadOnlyList<CalendarEventDto>> GetCalendarAsync(Guid yearId, Guid? orgUnitId, CancellationToken ct = default) =>
        await http.GetAsync<List<CalendarEventDto>>(
            orgUnitId is { } id ? $"{Routes.AcademicYearCalendar(yearId)}?orgUnitId={id}" : Routes.AcademicYearCalendar(yearId), ct);

    public async Task<CalendarEventDto> CreateEventAsync(SaveCalendarEventRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<CalendarEventDto>(HttpMethod.Post, Routes.CalendarEvents, request, ct))!;

    public async Task<CalendarEventDto> UpdateEventAsync(Guid id, SaveCalendarEventRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<CalendarEventDto>(HttpMethod.Put, Routes.CalendarEvent(id), request, ct))!;

    public Task DeleteEventAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.CalendarEvent(id), null, ct);
}
