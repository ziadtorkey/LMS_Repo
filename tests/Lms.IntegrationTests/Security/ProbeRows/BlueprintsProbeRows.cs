using Lms.IntegrationTests.Academic;
using Lms.Shared;
using Lms.Shared.Blueprints;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Blueprints (M3 Task 16): the course owner unit decides. A School1-owned course is invisible to School2's admin and
/// to client B (404); a Teacher reads published versions (unit-wide) but may not edit or publish (403); OrgAdmins edit and publish at their unit (D46).
/// </summary>
public sealed class BlueprintsProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("blueprint versions list (School1 course)", HttpMethod.Get, Routes.CourseBlueprintsPattern,
            async (w, _) => new ProbeRequest(Routes.CourseBlueprints(await PublishedCourseAsync(w))),
            Expect(Ok, Ok, NotFound, Ok, NotFound, Ok)),
        new("blueprint version get (School1 course)", HttpMethod.Get, Routes.CourseBlueprintVersionPattern,
            async (w, _) => new ProbeRequest(Routes.CourseBlueprintVersion(await PublishedCourseAsync(w), 1)),
            Expect(Ok, Ok, NotFound, Ok, NotFound, Ok)),
        new("blueprint draft create (School1 course)", HttpMethod.Post, Routes.CourseBlueprintDraftPattern,
            async (w, _) => new ProbeRequest(Routes.CourseBlueprintDraft(await CourseAsync(w))),
            Expect(Created, Created, NotFound, Forbidden, NotFound, Created)),
        new("blueprint draft get (School1 course)", HttpMethod.Get, Routes.CourseBlueprintDraftPattern,
            async (w, _) => new ProbeRequest(Routes.CourseBlueprintDraft(await DraftCourseAsync(w))),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("blueprint draft save (School1 course)", HttpMethod.Put, Routes.CourseBlueprintDraftPattern,
            async (w, _) => new ProbeRequest(Routes.CourseBlueprintDraft(await DraftCourseAsync(w)),
                new SaveBlueprintDraftRequest(1, [new BlueprintSectionInput(null, "قسم", "Section", [new BlueprintLessonInput(null, "درس", "Lesson", null, null, null)])])),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("blueprint draft delete (School1 course)", HttpMethod.Delete, Routes.CourseBlueprintDraftPattern,
            async (w, _) => new ProbeRequest(Routes.CourseBlueprintDraft(await DraftCourseAsync(w))),
            Expect(NoContent, NoContent, NotFound, Forbidden, NotFound, NoContent)),
        new("blueprint draft copy-from (School1 course)", HttpMethod.Post, Routes.CourseBlueprintCopyFromPattern,
            async (w, _) =>
            {
                var source = await PublishedCourseAsync(w);
                return new ProbeRequest(Routes.CourseBlueprintCopyFrom(await DraftCourseAsync(w)), new CopyBlueprintRequest(source, null));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("blueprint publish (School1 course)", HttpMethod.Post, Routes.CourseBlueprintPublishPattern,
            async (w, _) =>
            {
                var course = await DraftCourseAsync(w);
                await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Put, Routes.CourseBlueprintDraft(course),
                    new SaveBlueprintDraftRequest(1, [new BlueprintSectionInput(null, "قسم", "Section", [new BlueprintLessonInput(null, "درس", "Lesson", null, null, null)])]));
                return new ProbeRequest(Routes.CourseBlueprintPublish(course), new PublishBlueprintRequest(BlueprintApplyModes.NewClassesOnly));
            },
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
    ];

    private static async Task<Guid> CourseAsync(M1ProbeWorld w) =>
        (await AcademicTestKit.AddCourseAsync(w.Callers.Factory, w.Callers.A.School1)).Id;

    private static async Task<Guid> DraftCourseAsync(M1ProbeWorld w)
    {
        var course = await CourseAsync(w);
        await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.CourseBlueprintDraft(course));
        return course;
    }

    private static async Task<Guid> PublishedCourseAsync(M1ProbeWorld w)
    {
        var course = await DraftCourseAsync(w);
        await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Put, Routes.CourseBlueprintDraft(course),
            new SaveBlueprintDraftRequest(1, [new BlueprintSectionInput(null, "قسم", "Section", [new BlueprintLessonInput(null, "درس", "Lesson", null, null, null)])]));
        await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.CourseBlueprintPublish(course), new PublishBlueprintRequest(BlueprintApplyModes.NewClassesOnly));
        return course;
    }
}
