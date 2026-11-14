using Lms.IntegrationTests.Timetable;
using Lms.Shared;
using Lms.Shared.Attendance;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Taking attendance (M5 Task 17): records, save and history of one session. Admins reach the sessions of their subtree; the Teacher of the probe world teaches
/// none of the class courses (404, never a leak); School 2 and the other client reach none (404).
/// </summary>
public sealed class TakeAttendanceProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("attendance records read", HttpMethod.Get, Routes.AttendanceSessionRecordsPattern,
            async (w, _) => new ProbeRequest(Routes.AttendanceSessionRecords(await NewSessionAsync(w))),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("attendance records save", HttpMethod.Put, Routes.AttendanceSessionRecordsPattern,
            async (w, _) => new ProbeRequest(Routes.AttendanceSessionRecords(await NewSessionAsync(w)), new TakeAttendanceRequest(AttendanceStatuses.Present, [], null, 1)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("attendance history read", HttpMethod.Get, Routes.AttendanceSessionHistoryPattern,
            async (w, _) => new ProbeRequest(Routes.AttendanceSessionHistory(await NewSessionAsync(w))),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("attendance sessions of a unit", HttpMethod.Get, Routes.OrgUnitAttendanceSessionsPattern,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.OrgUnitAttendanceSessions(w.Callers.A.School1.Id, Today.AddDays(-1), Today))),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("attendance report", HttpMethod.Get, Routes.AttendanceReportPattern,
            async (w, _) => new ProbeRequest($"{Routes.AttendanceReport(AttendanceReportKinds.Class)}?classId={(await TimetableKit.NewWorldAsync(w.Callers.Factory, w.Callers.A.School1)).Class.Id}"),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, NotFound)),
        // A guardian or student reaches only their own child (404 for anyone else's); staff hold no Attendance.ViewOwn (403). The link rule is tested in AttendanceReadTests.
        new("attendance of a child", HttpMethod.Get, Routes.StudentAttendancePattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.StudentAttendance(Guid.NewGuid()))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
    ];

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)); // Asia/Riyadh (D64)

    private static async Task<Guid> NewSessionAsync(M1ProbeWorld w)
    {
        var world = await TimetableKit.NewWorldAsync(w.Callers.Factory, w.Callers.A.School1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)); // Asia/Riyadh (D64)
        var created = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.ClassCourseAttendanceSessionsPattern.Replace("{id:guid}", world.Math.Id.ToString()),
            new CreateAdHocAttendanceSessionRequest(today, world.First.Id));
        return created.GetProperty("id").GetGuid();
    }
}
