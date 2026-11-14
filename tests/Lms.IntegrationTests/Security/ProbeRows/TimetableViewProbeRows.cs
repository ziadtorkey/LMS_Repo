using Lms.IntegrationTests.People;
using Lms.Shared;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The own-data timetable views (M5 Task 15): Timetable.ViewOwn is held by teachers, students and guardians only, so the admins are refused (403). The day range is
/// valid; a student who is not the caller own child is a 404 for the Teacher, and a platform admin has no client (404). Reading a child timetable as that child
/// guardian is covered by TimetableViewTests (the probe world has no guardian caller).
/// </summary>
public sealed class TimetableViewProbeRows : IProbeRowModule
{
    private static readonly DateOnly From = new(2041, 9, 1);
    private static readonly DateOnly To = new(2041, 9, 7);

    public IEnumerable<ProbeRow> Rows =>
    [
        new("own timetable", HttpMethod.Get, Routes.MeTimetable,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.MyTimetable(From, To))),
            Expect(Forbidden, Forbidden, Forbidden, Ok, Forbidden, NotFound)),
        new("student timetable", HttpMethod.Get, Routes.StudentTimetablePattern,
            async (w, _) =>
            {
                var student = await PeopleTestKit.AddStudentAsync(w.Callers.Factory, w.Callers.A.School1, PeopleTestKit.NewCode());
                return new ProbeRequest(Routes.StudentTimetable(student.Id, From, To));
            },
            Expect(Forbidden, Forbidden, Forbidden, NotFound, Forbidden, NotFound)),
    ];
}
