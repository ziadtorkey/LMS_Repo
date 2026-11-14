using Lms.IntegrationTests.Timetable;
using Lms.Shared;
using Lms.Shared.Attendance;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Attendance sessions of a teacher (M5 Task 16): the day list answers for the caller only (a platform admin has no client, so 404), and an ad hoc session
/// needs Attendance.Take and the relationship to the class course: admins reach class courses of their subtree, the Teacher of the probe world teaches none
/// of them (404), School 2 and the other client reach none (404).
/// </summary>
public sealed class AttendanceSessionProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("attendance sessions of the day", HttpMethod.Get, Routes.AttendanceSessionsPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.AttendanceSessionsPattern)),
            Expect(Ok, Ok, Ok, Ok, Ok, NotFound)),
        new("attendance ad hoc session", HttpMethod.Post, Routes.ClassCourseAttendanceSessionsPattern,
            async (w, _) =>
            {
                var world = await TimetableKit.NewWorldAsync(w.Callers.Factory, w.Callers.A.School1);
                var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)); // Asia/Riyadh (D64)
                while (today.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
                {
                    today = today.AddDays(-1); // ad hoc sessions are for school days
                }

                return new ProbeRequest(Routes.ClassCourseAttendanceSessionsPattern.Replace("{id:guid}", world.Math.Id.ToString()),
                    new CreateAdHocAttendanceSessionRequest(today, world.First.Id));
            },
            Expect(Created, Created, NotFound, NotFound, NotFound, Created)),
    ];
}
