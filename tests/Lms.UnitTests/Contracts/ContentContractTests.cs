using System.Text.Json;
using Lms.Shared;
using Lms.Shared.Classes;
using Lms.Shared.Content;

namespace Lms.UnitTests.Contracts;

/// <summary>M4a contract, wave 1 (plan Task 1): module, asset, completion and progress catalogs, DTOs and routes.</summary>
public sealed class ContentContractTests
{
    private static readonly JsonSerializerOptions Web = JsonSerializerOptions.Web;

    [Fact]
    public void Module_catalogs_match_the_plan()
    {
        Assert.Equal(
            ["File", "Video", "Audio", "Page", "Link", "Assignment", "Quiz", "LibraryResource", "LiveSession", "Scorm", "H5p"],
            ModuleKinds.All);
        Assert.Equal(["File", "Video", "Audio", "Page", "Link"], ModuleKinds.Content);
        Assert.Equal(["Published", "Hidden", "Scheduled"], ModuleVisibilities.All);
        Assert.Equal(["ClassCourse", "Group"], ModuleTargets.All);
        Assert.Equal(["None", "Manual", "View", "Submit", "Score"], CompletionRules.All);
        Assert.Equal(["None", "Manual", "View"], CompletionRules.ForContent);
        Assert.Equal(["NotStarted", "InProgress", "Completed"], CompletionStates.All);
        Assert.Equal(["Document", "Image", "Audio", "Video"], AssetKinds.All);
    }

    [Fact]
    public void Catalog_subsets_are_inside_their_catalogs_and_values_are_unique()
    {
        foreach (var list in new[]
                 {
                     ModuleKinds.All, ModuleVisibilities.All, ModuleTargets.All, CompletionRules.All, CompletionStates.All,
                     AssetKinds.All,
                 })
        {
            Assert.Equal(list.Count, list.Distinct(StringComparer.Ordinal).Count());
        }

        Assert.All(ModuleKinds.Content, k => Assert.Contains(k, ModuleKinds.All));
        Assert.All(CompletionRules.ForContent, r => Assert.Contains(r, CompletionRules.All));
    }

    [Fact]
    public void Lti_is_not_a_module_kind() =>
        Assert.DoesNotContain(ModuleKinds.All, k => k.Contains("Lti", StringComparison.OrdinalIgnoreCase));

    [Theory]
    [InlineData(ModuleKinds.File, new[] { "Document", "Image" })]
    [InlineData(ModuleKinds.Video, new[] { "Video" })]
    [InlineData(ModuleKinds.Audio, new[] { "Audio" })]
    [InlineData(ModuleKinds.Page, new string[0])]
    [InlineData(ModuleKinds.Link, new string[0])]
    [InlineData(ModuleKinds.Quiz, new string[0])]
    public void Content_kinds_accept_the_planned_asset_kinds(string moduleKind, string[] expected) =>
        Assert.Equal(expected, AssetKinds.AcceptedBy(moduleKind));

    [Fact]
    public void Asset_type_allow_list_is_well_formed_and_has_no_script_types()
    {
        Assert.NotEmpty(AssetTypes.All);
        Assert.Equal(AssetTypes.All.Count, AssetTypes.All.Select(t => t.Extension).Distinct(StringComparer.Ordinal).Count());
        Assert.All(AssetTypes.All, t =>
        {
            Assert.StartsWith(".", t.Extension, StringComparison.Ordinal);
            Assert.Equal(t.Extension.ToLowerInvariant(), t.Extension);
            Assert.Contains(t.Kind, AssetKinds.All);
            Assert.Contains('/', t.ContentType);
        });
        Assert.All(AssetKinds.All, k => Assert.Contains(AssetTypes.All, t => t.Kind == k));

        string[] forbidden = [".svg", ".html", ".htm", ".js", ".xhtml", ".exe"];
        Assert.DoesNotContain(AssetTypes.All, t => forbidden.Contains(t.Extension));
        Assert.DoesNotContain(AssetTypes.All, t => t.ContentType.Contains("svg", StringComparison.Ordinal)
            || t.ContentType.Contains("html", StringComparison.Ordinal) || t.ContentType.Contains("javascript", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Lesson 1.PDF", ".pdf", "Document")]
    [InlineData("intro.mp4", ".mp4", "Video")]
    [InlineData("photo.final.JPEG", ".jpeg", "Image")]
    public void ForFileName_finds_the_type_case_insensitively(string fileName, string extension, string kind)
    {
        var type = AssetTypes.ForFileName(fileName);

        Assert.NotNull(type);
        Assert.Equal((extension, kind), (type.Extension, type.Kind));
    }

    [Theory]
    [InlineData("drawing.svg")]
    [InlineData("page.html")]
    [InlineData("no-extension")]
    [InlineData("archive.zip")]
    public void ForFileName_rejects_unknown_types(string fileName) => Assert.Null(AssetTypes.ForFileName(fileName));

    [Fact]
    public void Content_routes_are_unique_and_start_with_api()
    {
        var id = Guid.Parse("0198a6f2-0000-7000-8000-000000000008");
        var other = Guid.Parse("0198a6f2-0000-7000-8000-000000000009");

        Assert.Equal("/api/class-courses/{id:guid}/lessons/{lessonKey:guid}/modules", Routes.ClassCourseLessonModulesPattern);
        Assert.Equal("/api/class-courses/{id:guid}/lessons/{lessonKey:guid}/modules/order", Routes.ClassCourseLessonModulesOrderPattern);
        Assert.Equal("/api/class-courses/{id:guid}/progress", Routes.ClassCourseProgressPattern);
        Assert.Equal("/api/modules/{id:guid}", Routes.ModulePattern);
        Assert.Equal("/api/modules/{id:guid}/move", Routes.ModuleMovePattern);
        Assert.Equal("/api/modules/{id:guid}/completion", Routes.ModuleCompletionPattern);
        Assert.Equal("/api/assets", Routes.Assets);
        Assert.Equal("/api/assets/limits", Routes.AssetsLimits);
        Assert.Equal("/api/assets/{id:guid}", Routes.AssetPattern);
        Assert.Equal("/api/assets/{id:guid}/url", Routes.AssetUrlPattern);

        Assert.Equal($"/api/class-courses/{id}/lessons/{other}/modules", Routes.ClassCourseLessonModules(id, other));
        Assert.Equal($"/api/class-courses/{id}/lessons/{other}/modules/order", Routes.ClassCourseLessonModulesOrder(id, other));
        Assert.Equal($"/api/class-courses/{id}/progress?studentId={other}", Routes.ClassCourseProgressOf(id, other));
        Assert.Equal($"/api/modules/{id}/completion", Routes.ModuleCompletion(id));
        Assert.Equal($"/api/modules/{id}/move", Routes.ModuleMove(id));
        Assert.Equal($"/api/assets/{id}/url", Routes.AssetUrl(id));
        Assert.Equal($"/api/assets/limits?classCourseId={id}", Routes.AssetsLimitsFor(id));
        Assert.Equal(("file", "classCourseId"), (AssetUploadForm.File, AssetUploadForm.ClassCourseId));

        StudentContractTests.AssertUniqueApiRoutes();
    }

    [Fact]
    public void Module_dtos_round_trip()
    {
        var group = Guid.NewGuid();
        var node = new ModuleNodeDto(Guid.NewGuid(), ModuleKinds.Page, 1, "ملخص", "Summary", ModuleVisibilities.Scheduled,
            new DateTime(2026, 11, 15, 6, 0, 0, DateTimeKind.Utc), null, ModuleTargets.Group, group, "المجموعة أ", "Group A",
            CompletionRules.Manual, false, CompletionStates.NotStarted);
        var asset = new AssetDto(Guid.NewGuid(), Guid.NewGuid(), AssetKinds.Video, "intro.mp4", "video/mp4", 1_048_576, 95, true,
            new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc));
        var detail = new ModuleDetailDto(node, asset.ClassCourseId!.Value, Guid.NewGuid(), null,
            new ModuleContentDto(asset, null, null, null, null), null, Guid.NewGuid(), true, DateTime.UtcNow);
        var create = new CreateModuleRequest(ModuleKinds.Link, "رابط", "Link", ModuleVisibilities.Published, null, null,
            ModuleTargets.ClassCourse, null, CompletionRules.View, null, new ModuleContentInput(Url: "https://example.org"));

        Assert.Equal(node, RoundTrip(node));
        Assert.Equal(group, RoundTrip(detail).Node.GroupId);
        Assert.Equal(95, RoundTrip(detail).Content.Asset!.DurationSeconds);
        Assert.Equal("https://example.org", RoundTrip(create).Content.Url);
        Assert.Null(new ModuleContentInput().AssetId);
        Assert.Equal(2, RoundTrip(new ReorderModulesRequest([Guid.NewGuid(), Guid.NewGuid()])).ModuleIds.Count);
    }

    [Fact]
    public void Progress_and_lesson_plan_dtos_round_trip()
    {
        var module = Guid.NewGuid();
        var lesson = Guid.NewGuid();
        var completion = new ModuleCompletionDto(module, CompletionRules.Manual, CompletionStates.Completed,
            new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc), null);
        var progress = new CourseProgressDto(Guid.NewGuid(), Guid.NewGuid(), new ProgressDto(1, 4),
            [new LessonProgressDto(lesson, new ProgressDto(1, 2))], [completion], Guid.NewGuid(), DateTime.UtcNow);
        var node = new ModuleNodeDto(module, ModuleKinds.File, 1, "ملف", "File", ModuleVisibilities.Published, null, null,
            ModuleTargets.ClassCourse, null, null, null, CompletionRules.Manual, true, CompletionStates.Completed);
        var legacyLesson = new ClassLessonDto(lesson, 1, "الدرس", "Lesson", null, null, null, false);
        var plan = new ClassLessonPlanDto(Guid.NewGuid(), 1, null, "Week",
            [new ClassSectionDto(Guid.NewGuid(), 1, "الأسبوع ١", "Week 1", false,
                [legacyLesson with { Modules = [node], Progress = new ProgressDto(1, 1) }])],
            new ProgressDto(1, 1), DateTime.UtcNow);

        Assert.Equal(completion, RoundTrip(completion));
        Assert.Equal(4, RoundTrip(progress).Course.Total);
        Assert.Equal(lesson, RoundTrip(progress).Lessons[0].LessonKey);
        Assert.Null(legacyLesson.Modules);
        Assert.Null(legacyLesson.Progress);
        var lessonBack = RoundTrip(plan).Sections[0].Lessons[0];
        Assert.Equal(node, lessonBack.Modules![0]);
        Assert.Equal(new ProgressDto(1, 1), lessonBack.Progress);
        Assert.Null(new ClassLessonPlanDto(Guid.NewGuid(), null, null, "Week", []).ServerNowUtc);
    }

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Web), Web)!;
}
