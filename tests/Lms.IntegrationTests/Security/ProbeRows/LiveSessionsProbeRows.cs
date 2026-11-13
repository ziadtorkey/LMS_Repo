using Lms.Domain.Classes;
using Lms.Domain.Configuration;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Classes;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Blueprints;
using Lms.Shared.Configuration;
using Lms.Shared.Content;
using Lms.Shared.LiveSessions;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Live sessions of a class course (M4b Task 4): everything hangs off a class course at School1. School 2 and the other client
/// get 404 (an invisible unit); a Teacher without an assignment to the class course gets 404 (relationship). The
/// <c>VirtualClasses</c> flag is on for the probe client so writes reach the handler.
/// </summary>
public sealed class LiveSessionsProbeRows : IProbeRowModule
{
    private static readonly Guid Section = Guid.NewGuid(), Lesson1 = Guid.NewGuid();

    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = 4 });

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("class course live sessions list", HttpMethod.Get, Routes.ClassCourseLiveSessionsPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseLiveSessions((await NewClassCourseAsync(w)).Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("class course live session create", HttpMethod.Post, Routes.ClassCourseLiveSessionsPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseLiveSessions((await NewClassCourseAsync(w)).Id), CreateBody()),
            Expect(Created, Created, NotFound, NotFound, NotFound, Created)),
        new("live session get", HttpMethod.Get, Routes.LiveSessionPattern,
            async (w, _) => new ProbeRequest(Routes.LiveSession(await NewSessionAsync(w))),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("live session update", HttpMethod.Put, Routes.LiveSessionPattern,
            async (w, _) => new ProbeRequest(Routes.LiveSession(await NewSessionAsync(w)), UpdateBody()),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("live session delete", HttpMethod.Delete, Routes.LiveSessionPattern,
            async (w, _) => new ProbeRequest(Routes.LiveSession(await NewSessionAsync(w))),
            Expect(NoContent, NoContent, NotFound, NotFound, NotFound, NoContent)),
    ];

    private static ModuleSettingsInput Module() =>
        new("حصة", "Live", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.None, null);

    private static CreateLiveSessionRequest CreateBody() => new(Lesson1, Module(), DateTime.UtcNow.AddDays(3), 60);

    private static UpdateLiveSessionRequest UpdateBody() => new(Module(), DateTime.UtcNow.AddDays(4), 45);

    /// <summary>A class course with a one-lesson plan at School1; virtual classes are on for the client.</summary>
    private static async Task<ClassCourse> NewClassCourseAsync(M1ProbeWorld w)
    {
        var factory = w.Callers.Factory;
        var tree = w.Callers.A;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            if (!db.Set<OrgUnitSetting>().Any(s => s.OrganizationId == tree.Org.Id && s.Key == FeatureFlags.VirtualClasses.Name))
            {
                db.Add(new OrgUnitSetting(tree.Org.Id, tree.Root.Id, tree.Root.Path, FeatureFlags.VirtualClasses.Name, "true", false));
                await db.SaveChangesAsync();
            }
        }

        var year = await AcademicTestKit.AddYearAsync(factory, tree.School1);
        var cls = await AcademicTestKit.AddClassAsync(factory, tree.School1, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, tree.School1);
        await LessonPlanTestKit.PublishAsync(factory, course, BlueprintApplyModes.PushToExisting,
            new TestSection(Section, "Week 1", new TestLesson(Lesson1, "Intro")));
        return await LessonPlanTestKit.AddClassCourseWithPlanAsync(factory, cls, course);
    }

    private static async Task<Guid> NewSessionAsync(M1ProbeWorld w)
    {
        var classCourse = await NewClassCourseAsync(w);
        var created = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.ClassCourseLiveSessions(classCourse.Id), CreateBody());
        return created[0].GetProperty("id").GetGuid();
    }
}
