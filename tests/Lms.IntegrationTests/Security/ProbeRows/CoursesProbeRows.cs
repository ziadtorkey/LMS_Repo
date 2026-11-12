using Lms.Shared;
using Lms.Shared.Courses;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Courses (M3 Task 6): owned by the client root in the probe world. Schools read the root course as inherited but can
/// neither change nor delete it (404: it is outside their unit); another client never sees it; a Teacher cannot write.
/// </summary>
public sealed class CoursesProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("courses list", HttpMethod.Get, Routes.Courses,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Courses)),
            Expect(Ok, Ok, Ok, Ok, Ok, Ok)),
        new("courses get (root-owned)", HttpMethod.Get, Routes.CoursePattern,
            async (w, _) => new ProbeRequest(Routes.Course(await NewCourseAsync(w))),
            Expect(Ok, Ok, Ok, Ok, NotFound, Ok)),
        new("courses create (owner root)", HttpMethod.Post, Routes.Courses,
            (w, _) => Task.FromResult(new ProbeRequest(Routes.Courses, Body(w))),
            Expect(Created, NotFound, NotFound, Forbidden, NotFound, Created)),
        new("courses update (root-owned)", HttpMethod.Put, Routes.CoursePattern,
            async (w, _) => new ProbeRequest(Routes.Course(await NewCourseAsync(w)), Body(w)),
            Expect(Ok, NotFound, NotFound, Forbidden, NotFound, Ok)),
        new("courses status (root-owned)", HttpMethod.Put, Routes.CourseStatusPattern,
            async (w, _) => new ProbeRequest(Routes.CourseStatus(await NewCourseAsync(w)), new SetCourseStatusRequest(CourseStatuses.Inactive)),
            Expect(Ok, NotFound, NotFound, Forbidden, NotFound, Ok)),
        new("courses delete (root-owned)", HttpMethod.Delete, Routes.CoursePattern,
            async (w, _) => new ProbeRequest(Routes.Course(await NewCourseAsync(w))),
            Expect(NoContent, NotFound, NotFound, Forbidden, NotFound, NoContent)),
    ];

    private static SaveCourseRequest Body(M1ProbeWorld w)
    {
        var n = w.Next();
        return new SaveCourseRequest($"PC{n}-{Guid.NewGuid():N}"[..20], "مادة " + n, "Probe course " + n, null, null, "en", 3, w.Callers.A.Root.Id, null);
    }

    private static async Task<Guid> NewCourseAsync(M1ProbeWorld w) =>
        (await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.Courses, Body(w))).GetProperty("summary").GetProperty("id").GetGuid();
}
