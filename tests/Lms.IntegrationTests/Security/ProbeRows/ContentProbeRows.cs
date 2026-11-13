using Lms.Domain.Classes;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Classes;
using Lms.Shared;
using Lms.Shared.Blueprints;
using Lms.Shared.Content;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Lesson modules, completion and progress (M4a Tasks 3 and 6): everything hangs off a class course at School1. School 2 and the
/// other client get 404 (an invisible unit); a Teacher without an assignment to the class course gets 404 (relationship). The
/// completion routes belong to a student: a staff caller with the permission is refused (403) after the resource is found.
/// </summary>
public sealed class ContentProbeRows : IProbeRowModule
{
    private static readonly Guid Section = Guid.NewGuid(), Lesson1 = Guid.NewGuid(), Lesson2 = Guid.NewGuid();

    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = ProbeMilestones.M4a });

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("class lesson modules list", HttpMethod.Get, Routes.ClassCourseLessonModulesPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseLessonModules((await NewClassCourseAsync(w)).Id, Lesson1)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("class lesson module create", HttpMethod.Post, Routes.ClassCourseLessonModulesPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseLessonModules((await NewClassCourseAsync(w)).Id, Lesson1), PageBody()),
            Expect(Created, Created, NotFound, NotFound, NotFound, Created)),
        new("class lesson modules reorder", HttpMethod.Put, Routes.ClassCourseLessonModulesOrderPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseLessonModulesOrder((await NewClassCourseAsync(w)).Id, Lesson1), new ReorderModulesRequest([])),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("module get", HttpMethod.Get, Routes.ModulePattern,
            async (w, _) => new ProbeRequest(Routes.Module(await NewModuleAsync(w))),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("module update", HttpMethod.Put, Routes.ModulePattern,
            async (w, _) => new ProbeRequest(Routes.Module(await NewModuleAsync(w)), UpdateBody()),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("module delete", HttpMethod.Delete, Routes.ModulePattern,
            async (w, _) => new ProbeRequest(Routes.Module(await NewModuleAsync(w))),
            Expect(NoContent, NoContent, NotFound, NotFound, NotFound, NoContent)),
        new("module move", HttpMethod.Post, Routes.ModuleMovePattern,
            async (w, _) => new ProbeRequest(Routes.ModuleMove(await NewModuleAsync(w)), new MoveModuleRequest(Lesson2)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("module completion get", HttpMethod.Get, Routes.ModuleCompletionPattern,
            async (w, _) => new ProbeRequest(Routes.ModuleCompletion(await NewModuleAsync(w))),
            Expect(Forbidden, Forbidden, NotFound, NotFound, NotFound, Forbidden)),
        new("module completion mark", HttpMethod.Put, Routes.ModuleCompletionPattern,
            async (w, _) => new ProbeRequest(Routes.ModuleCompletion(await NewModuleAsync(w))),
            Expect(Forbidden, Forbidden, NotFound, NotFound, NotFound, Forbidden)),
        new("class course progress", HttpMethod.Get, Routes.ClassCourseProgressPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseProgress((await NewClassCourseAsync(w)).Id)),
            Expect(BadRequest, BadRequest, NotFound, NotFound, NotFound, BadRequest)), // staff must name the student
    ];

    private static CreateModuleRequest PageBody() =>
        new(ModuleKinds.Page, "صفحة", "Page", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.None, null,
            new ModuleContentInput(BodyHtmlEn: "<p>Probe</p>"));

    private static UpdateModuleRequest UpdateBody() =>
        new("صفحة", "Renamed", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.None, null,
            new ModuleContentInput(BodyHtmlEn: "<p>Probe</p>"));

    /// <summary>A class course with a two-lesson plan at School1.</summary>
    private static async Task<ClassCourse> NewClassCourseAsync(M1ProbeWorld w)
    {
        var factory = w.Callers.Factory;
        var school1 = w.Callers.A.School1;
        var year = await AcademicTestKit.AddYearAsync(factory, school1);
        var cls = await AcademicTestKit.AddClassAsync(factory, school1, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, school1);
        await LessonPlanTestKit.PublishAsync(factory, course, BlueprintApplyModes.PushToExisting,
            new TestSection(Section, "Week 1", new TestLesson(Lesson1, "Intro"), new TestLesson(Lesson2, "Basics")));
        return await LessonPlanTestKit.AddClassCourseWithPlanAsync(factory, cls, course);
    }

    private static async Task<Guid> NewModuleAsync(M1ProbeWorld w)
    {
        var classCourse = await NewClassCourseAsync(w);
        var created = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.ClassCourseLessonModules(classCourse.Id, Lesson1), PageBody());
        return created.GetProperty("node").GetProperty("id").GetGuid();
    }
}
