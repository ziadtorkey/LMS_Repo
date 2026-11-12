using Lms.Shared;
using Lms.Shared.Courses;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Course supervisors (M3 Task 9): the course is owned by the client root. Only the root's Org Admin manages them
/// (Courses.ManageSupervisors at the owner unit); schools read the course as inherited, so for them it is outside their unit (404);
/// a Teacher lacks the permission; another client never sees the course.
/// </summary>
public sealed class CourseSupervisorsProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("course supervisors set (root-owned)", HttpMethod.Put, Routes.CourseSupervisorsPattern,
            async (w, _) => new ProbeRequest(Routes.CourseSupervisors(await NewCourseAsync(w)), new SetCourseSupervisorsRequest([])),
            Expect(Ok, NotFound, NotFound, Forbidden, NotFound, Ok)),
    ];

    private static async Task<Guid> NewCourseAsync(M1ProbeWorld w)
    {
        var n = w.Next();
        var body = new SaveCourseRequest($"PS{n}-{Guid.NewGuid():N}"[..20], "مادة " + n, "Probe supervised course " + n, null, null, "en", 3, w.Callers.A.Root.Id, null);
        return (await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.Courses, body)).GetProperty("summary").GetProperty("id").GetGuid();
    }
}
