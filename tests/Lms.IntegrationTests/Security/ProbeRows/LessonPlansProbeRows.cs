using Lms.Domain.Classes;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Classes;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The class lesson plan tree (M3 Task 17, #225): <c>GET /api/class-courses/{id}/lesson-plan</c> needs LessonPlans.View at the
/// class unit (School1); School 2 and the other client get 404; a Teacher reaches only class courses they teach (the probe's
/// has no teacher assignment: 404). A platform admin is unrestricted.
/// </summary>
public sealed class LessonPlansProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("lesson plan tree", HttpMethod.Get, Routes.ClassCourseLessonPlanPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseLessonPlan((await NewAsync(w)).Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
    ];

    private static async Task<ClassCourse> NewAsync(M1ProbeWorld w)
    {
        var factory = w.Callers.Factory;
        var school1 = w.Callers.A.School1;
        var year = await AcademicTestKit.AddYearAsync(factory, school1);
        var cls = await AcademicTestKit.AddClassAsync(factory, school1, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, school1);
        return await LessonPlanTestKit.AddClassCourseWithPlanAsync(factory, cls, course);
    }
}
