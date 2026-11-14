using Lms.Domain.Calendar;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Timetable;
using Lms.Shared;
using Lms.Shared.Attendance;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The live proposal calls (M5 Task 18): confirm and map guests. The probe session is an ad hoc one, so a caller who reaches it is answered 409
/// <c>attendance_not_a_proposal</c> (the rule is tested in LiveProposalTests); everyone else gets 404, never a leak.
/// </summary>
public sealed class LiveProposalProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("attendance proposal confirm", HttpMethod.Post, Routes.AttendanceSessionConfirmPattern,
            async (w, _) => new ProbeRequest(Routes.AttendanceSessionConfirm(await NewSessionAsync(w)), new ConfirmAttendanceRequest(1)),
            Expect(Conflict, Conflict, NotFound, NotFound, NotFound, Conflict)),
        new("attendance proposal live mappings", HttpMethod.Put, Routes.AttendanceSessionLiveMappingsPattern,
            async (w, _) => new ProbeRequest(Routes.AttendanceSessionLiveMappings(await NewSessionAsync(w)), new LiveMappingRequest([], 1)),
            Expect(Conflict, Conflict, NotFound, NotFound, NotFound, Conflict)),
    ];

    private static async Task<Guid> NewSessionAsync(M1ProbeWorld w)
    {
        var world = await TimetableKit.NewWorldAsync(w.Callers.Factory, w.Callers.A.School1);
        using var scope = new EngineScope(w.Callers.Factory.Services);
        var today = await scope.Services.GetRequiredService<IClientCalendar>().LocalTodayAsync(w.Callers.A.Org.Id, default);
        while (today.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
        {
            today = today.AddDays(-1); // ad hoc sessions are for school days
        }

        var created = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.ClassCourseAttendanceSessionsPattern.Replace("{id:guid}", world.Math.Id.ToString()),
            new CreateAdHocAttendanceSessionRequest(today, world.First.Id));
        return created.GetProperty("id").GetGuid();
    }
}
