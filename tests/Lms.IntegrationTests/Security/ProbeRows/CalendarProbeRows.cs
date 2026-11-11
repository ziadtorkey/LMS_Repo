using Lms.Shared;
using Lms.Shared.AcademicYears;
using static Lms.IntegrationTests.Security.AcademicYearsProbeRows;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>Academic calendar (Task 22, #115/#124): events at the year's unit; checked at the event's unit.</summary>
public sealed class CalendarProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("calendar read", HttpMethod.Get, Routes.AcademicYearCalendarPattern,
            async (w, _) =>
            {
                var (yearId, unitId) = await NewYearAsync(w);
                return new ProbeRequest($"{Routes.AcademicYearCalendar(yearId)}?orgUnitId={unitId}");
            },
            Expect(Ok, Ok, NotFound, Ok, NotFound, Ok)),
        new("calendar-events create", HttpMethod.Post, Routes.CalendarEvents,
            async (w, _) =>
            {
                var (yearId, unitId) = await NewYearAsync(w);
                return new ProbeRequest(Routes.CalendarEvents, EventBody(yearId, unitId));
            },
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("calendar-events update", HttpMethod.Put, Routes.CalendarEventPattern,
            async (w, _) =>
            {
                var (eventId, yearId, unitId) = await NewEventAsync(w);
                return new ProbeRequest(Routes.CalendarEvent(eventId), EventBody(yearId, unitId));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("calendar-events delete", HttpMethod.Delete, Routes.CalendarEventPattern,
            async (w, _) => new ProbeRequest(Routes.CalendarEvent((await NewEventAsync(w)).EventId)),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),
    ];

    private static SaveCalendarEventRequest EventBody(Guid yearId, Guid unitId) =>
        new(yearId, unitId, "عطلة", "Probe holiday", CalendarEventKinds.Holiday, YearStart.AddDays(30), YearStart.AddDays(31));

    private static async Task<(Guid EventId, Guid YearId, Guid UnitId)> NewEventAsync(M1ProbeWorld w)
    {
        var (yearId, unitId) = await NewYearAsync(w);
        var created = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.CalendarEvents, EventBody(yearId, unitId));
        return (created.GetProperty("id").GetGuid(), yearId, unitId);
    }
}
