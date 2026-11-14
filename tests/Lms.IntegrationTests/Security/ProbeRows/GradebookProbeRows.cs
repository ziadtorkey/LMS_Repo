using Lms.IntegrationTests.Grading;
using Lms.Shared;
using Lms.Shared.Grading;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The staff gradebook of a class course at School1 (M5 Task 8): School 2 and the other client get 404 (an invisible unit), a Teacher with no
/// assignment to the class course gets 404 (relationship); admins read and write, the platform admin too.
/// </summary>
public sealed class GradebookProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = ProbeMilestones.M5 });

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("gradebook grid", HttpMethod.Get, Routes.ClassCourseGradebookPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseGradebook((await NewAsync(w)).ClassCourse.Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("gradebook config", HttpMethod.Put, Routes.ClassCourseGradebookConfigPattern,
            async (w, _) => new ProbeRequest(
                Routes.ClassCourseGradebookConfig((await NewAsync(w)).ClassCourse.Id),
                new SaveGradebookConfigRequest(null, 2, CategoryMethods.PointsWeighted, MissingPolicies.Normalise)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("gradebook categories", HttpMethod.Put, Routes.ClassCourseGradebookCategoriesPattern,
            async (w, _) =>
            {
                var cc = (await NewAsync(w)).ClassCourse.Id;
                var grid = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Get, Routes.ClassCourseGradebook(cc));
                var uncategorised = grid.GetProperty("categories")[0];
                return new ProbeRequest(Routes.ClassCourseGradebookCategories(cc), new SaveGradeCategoriesRequest(
                    [new(uncategorised.GetProperty("id").GetGuid(), "غير مصنف", "Uncategorised", 0, 0)]));
            },
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("gradebook item create", HttpMethod.Post, Routes.ClassCourseGradebookItemsPattern,
            async (w, _) =>
            {
                var cc = (await NewAsync(w)).ClassCourse.Id;
                return new ProbeRequest(Routes.ClassCourseGradebookItems(cc), new CreateGradeItemRequest(await CategoryAsync(w, cc), "اختبار", "Test", 10));
            },
            Expect(Created, Created, NotFound, NotFound, NotFound, Created)),
        new("gradebook item update", HttpMethod.Put, Routes.ClassCourseGradebookItemPattern,
            async (w, _) =>
            {
                var (cc, item, category) = await NewItemAsync(w);
                return new ProbeRequest(Routes.ClassCourseGradebookItem(cc, item), new UpdateGradeItemRequest(category, "اختبار", "Test", 10, 5));
            },
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("gradebook item delete", HttpMethod.Delete, Routes.ClassCourseGradebookItemPattern,
            async (w, _) =>
            {
                var (cc, item, _) = await NewItemAsync(w);
                return new ProbeRequest(Routes.ClassCourseGradebookItem(cc, item));
            },
            Expect(NoContent, NoContent, NotFound, NotFound, NotFound, NoContent)),
        new("gradebook item export", HttpMethod.Get, Routes.ClassCourseGradebookItemExportPattern,
            async (w, _) =>
            {
                var (cc, item, _) = await NewItemAsync(w);
                return new ProbeRequest(Routes.ClassCourseGradebookItemExport(cc, item));
            },
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        // the matrix cannot send multipart: a caller that passes every check reaches the form check and answers 400 grade_import_invalid
        new("gradebook item import preview", HttpMethod.Post, Routes.ClassCourseGradebookItemImportPreviewPattern,
            async (w, _) =>
            {
                var (cc, item, _) = await NewItemAsync(w);
                return new ProbeRequest(Routes.ClassCourseGradebookItemImportPreview(cc, item), new { });
            },
            Expect(BadRequest, BadRequest, NotFound, NotFound, NotFound, BadRequest)),
        new("gradebook item import apply", HttpMethod.Post, Routes.ClassCourseGradebookItemImportApplyPattern,
            async (w, _) =>
            {
                var (cc, item, _) = await NewItemAsync(w);
                return new ProbeRequest(Routes.ClassCourseGradebookItemImportApply(cc, item), new ApplyGradeImportRequest("not-a-token"));
            },
            Expect(BadRequest, BadRequest, NotFound, NotFound, NotFound, BadRequest)),
        new("gradebook entries", HttpMethod.Put, Routes.ClassCourseGradebookEntriesPattern,
            async (w, _) =>
            {
                var gb = await NewAsync(w);
                var (cc, item, _) = await NewItemAsync(w, gb);
                using var helper = new GradebookGridHost(w.Callers.Factory);
                var student = await helper.EnrollNewStudentAsync(w.Callers.A, gb);
                return new ProbeRequest(Routes.ClassCourseGradebookEntries(cc), new UpsertEntriesRequest(
                    student.Id, 0, [new GradeEntryChange(item, GradeEntryActions.Set, 5, null)]));
            },
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
    ];

    private static Task<GradebookTestKit.World> NewAsync(M1ProbeWorld w) => GradebookTestKit.NewWorldAsync(w.Callers.Factory, w.Callers.A.School1);

    private static async Task<Guid> CategoryAsync(M1ProbeWorld w, Guid classCourse) =>
        (await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Get, Routes.ClassCourseGradebook(classCourse))).GetProperty("categories")[0].GetProperty("id").GetGuid();

    private static async Task<(Guid ClassCourse, Guid Item, Guid Category)> NewItemAsync(M1ProbeWorld w, GradebookTestKit.World? gb = null)
    {
        var cc = (gb ?? await NewAsync(w)).ClassCourse.Id;
        var category = await CategoryAsync(w, cc);
        var item = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.ClassCourseGradebookItems(cc), new CreateGradeItemRequest(category, "اختبار", "Test", 10));
        return (cc, item.GetProperty("id").GetGuid(), category);
    }
}
