using System.Text.Json;
using Lms.Shared;
using Lms.Shared.Blueprints;
using Lms.Shared.Courses;

namespace Lms.UnitTests.Contracts;

/// <summary>M3 contract (plan Task 3): course, allocation, restriction, supervisor and blueprint catalogs, DTOs and routes.</summary>
public sealed partial class AcademicCatalogContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Restriction_types_and_apply_modes_are_unique()
    {
        AssertUnique(CourseRestrictionTypes.All);
        AssertUnique(BlueprintApplyModes.All);
        AssertUnique(CourseStatuses.All);
        AssertUnique(BlueprintStatuses.All);
        AssertUnique(CourseSorts.All);

        Assert.Equal(["Religion", "Gender", "LevelType"], CourseRestrictionTypes.All);
        Assert.Equal(["NewClassesOnly", "PushToExisting"], BlueprintApplyModes.All);
        Assert.Equal(["Active", "Inactive"], CourseStatuses.All);
        Assert.Equal(["Draft", "Published", "Superseded"], BlueprintStatuses.All);
        Assert.Equal(["code", "-code", "name", "-name"], CourseSorts.All);
    }

    [Fact]
    public void Course_routes_are_unique_and_start_with_api()
    {
        var id = Guid.Parse("0198a6f2-0000-7000-8000-000000000005");

        Assert.Equal("/api/courses", Routes.Courses);
        Assert.Equal("/api/courses/{id:guid}/status", Routes.CourseStatusPattern);
        Assert.Equal("/api/courses/{id:guid}/restrictions", Routes.CourseRestrictionsPattern);
        Assert.Equal("/api/courses/{id:guid}/supervisors", Routes.CourseSupervisorsPattern);
        Assert.Equal("/api/courses/{id:guid}/blueprints", Routes.CourseBlueprintsPattern);
        Assert.Equal("/api/courses/{id:guid}/blueprints/{version:int}", Routes.CourseBlueprintVersionPattern);
        Assert.Equal("/api/courses/{id:guid}/blueprints/draft", Routes.CourseBlueprintDraftPattern);
        Assert.Equal("/api/courses/{id:guid}/blueprints/draft/publish", Routes.CourseBlueprintPublishPattern);
        Assert.Equal("/api/courses/{id:guid}/blueprints/draft/copy-from", Routes.CourseBlueprintCopyFromPattern);
        Assert.Equal("/api/course-allocations/{id:guid}", Routes.CourseAllocationPattern);
        Assert.Equal("/api/org-units/{id:guid}/courses", Routes.UnitCoursesPattern);
        Assert.Equal("/api/me/supervised-courses", Routes.MySupervisedCourses);

        Assert.Equal($"/api/courses/{id}/blueprints/3", Routes.CourseBlueprintVersion(id, 3));
        Assert.Equal($"/api/courses/{id}/blueprints/draft/copy-from", Routes.CourseBlueprintCopyFrom(id));
        Assert.Equal($"/api/course-allocations/{id}", Routes.CourseAllocation(id));
        Assert.Equal($"/api/org-units/{id}/courses", Routes.UnitCourses(id));

        StudentContractTests.AssertUniqueApiRoutes();
    }

    [Fact]
    public void Course_and_blueprint_dtos_round_trip()
    {
        var summary = new CourseSummaryDto(Guid.NewGuid(), "MATH-1", "رياضيات", "Math", "ar", 5, Guid.NewGuid(), "الجذر", "Root",
            CourseStatuses.Active, true, 2, false);
        var detail = new CourseDetailDto(summary, null, "Basics",
            [new CourseRestrictionDto(CourseRestrictionTypes.Gender, ["Female"])],
            [new CourseSupervisorDto(Guid.NewGuid(), "مشرف", "Supervisor", null)], false);
        var save = new SaveCourseRequest("MATH-1", "رياضيات", "Math", null, null, "ar", 5, Guid.NewGuid(), null);
        var draft = new SaveBlueprintDraftRequest(4,
        [
            new BlueprintSectionInput(null, "الأسبوع ١", "Week 1",
                [new BlueprintLessonInput(Guid.NewGuid(), "الدرس", "Lesson", null, null, 7)]),
        ]);
        var blueprint = new BlueprintDto(
            new BlueprintSummaryDto(Guid.NewGuid(), summary.Id, 0, BlueprintStatuses.Draft, 4, null, null, null, null, 1, 1),
            [new BlueprintSectionDto(Guid.NewGuid(), 1, "الأسبوع ١", "Week 1", [new BlueprintLessonDto(Guid.NewGuid(), 1, "الدرس", "Lesson", null, null, 7)])]);

        Assert.Equal(summary, RoundTrip(summary));
        Assert.Equal("Female", RoundTrip(detail).Restrictions[0].Values[0]);
        Assert.Equal(save, RoundTrip(save));
        Assert.Equal(7, RoundTrip(draft).Sections[0].Lessons[0].PlannedOffsetDays);
        Assert.Null(RoundTrip(draft).Sections[0].Key);
        Assert.Equal(0, RoundTrip(blueprint).Summary.Version);
        Assert.Null(RoundTrip(new CopyBlueprintRequest(Guid.NewGuid(), null)).SourceVersion);
        var query = new CourseQuery(null);
        Assert.Equal((true, false, Paging.DefaultPageSize), (query.IncludeInherited, query.SupervisedByMe, query.PageSize));
        Assert.Single(RoundTrip(new UnitCoursesDto(
            [new CourseAllocationDto(Guid.NewGuid(), summary.Id, "MATH-1", "رياضيات", "Math", Guid.NewGuid(), "مدرسة", "School", true, 0)],
            [])).Available);
    }

    private static void AssertUnique(IReadOnlyList<string> values)
    {
        Assert.NotEmpty(values);
        Assert.Equal(values.Count, values.Distinct(StringComparer.Ordinal).Count());
    }

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Web), Web)!;
}
