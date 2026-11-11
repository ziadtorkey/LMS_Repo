using Lms.Shared;
using Lms.Shared.Classes;
using Lms.Shared.Enrollment;
using Lms.Shared.Imports;
using Lms.Shared.Teaching;

namespace Lms.UnitTests.Contracts;

/// <summary>M3 contract (plan Task 4): class, teaching, enrollment, group, transfer and lesson-plan catalogs, DTOs and routes.</summary>
public sealed partial class AcademicCatalogContractTests
{
    [Fact]
    public void Academic_catalog_values_are_unique()
    {
        AssertUnique(ClassStatuses.All);
        AssertUnique(ClassStudentStatuses.All);
        AssertUnique(EnrollmentSources.All);
        AssertUnique(EnrollmentStatuses.All);
        AssertUnique(TeacherRoles.All);
        AssertUnique(ExceptionStatuses.All);

        Assert.Equal(["Active", "Closed"], ClassStatuses.All);
        Assert.Equal(["Active", "TransferredOut", "Removed"], ClassStudentStatuses.All);
        Assert.Equal(["Auto", "Manual", "Exceptional", "Transfer"], EnrollmentSources.All);
        Assert.Equal(["Active", "Dropped"], EnrollmentStatuses.All);
        Assert.Equal(["Primary", "Assistant"], TeacherRoles.All);
        Assert.Equal(["Pending", "Approved", "Rejected", "Cancelled"], ExceptionStatuses.All);
    }

    [Fact]
    public void Academic_routes_are_unique_and_start_with_api()
    {
        var id = Guid.Parse("0198a6f2-0000-7000-8000-000000000006");
        var other = Guid.Parse("0198a6f2-0000-7000-8000-000000000007");

        Assert.Equal(("/api/classes/bulk", "/api/classes/copy", "/api/classes/matrix"),
            (Routes.ClassesBulk, Routes.ClassesCopy, Routes.ClassesMatrix));
        Assert.Equal("/api/classes/{id:guid}/close", Routes.ClassClosePattern);
        Assert.Equal("/api/classes/{id:guid}/students/{classStudentId:guid}/remove", Routes.ClassStudentRemovePattern);
        Assert.Equal("/api/classes/{id:guid}/courses", Routes.ClassCoursesOfClassPattern);
        Assert.Equal("/api/class-courses/bulk-assign", Routes.ClassCoursesBulkAssign);
        Assert.Equal("/api/class-courses/{id:guid}/teachers/{assignmentId:guid}/replace", Routes.ClassCourseTeacherReplacePattern);
        Assert.Equal("/api/class-courses/{id:guid}/teachers/{assignmentId:guid}/end", Routes.ClassCourseTeacherEndPattern);
        Assert.Equal("/api/class-courses/{id:guid}/lesson-plan", Routes.ClassCourseLessonPlanPattern);
        Assert.Equal("/api/teacher-eligibility/{id:guid}", Routes.TeacherEligibilityItemPattern);
        Assert.Equal("/api/teacher-assignments/bulk", Routes.TeacherAssignmentsBulk);
        Assert.Equal("/api/me/class-courses", Routes.MyClassCourses);
        Assert.Equal("/api/enrollments/{id:guid}/drop", Routes.EnrollmentDropPattern);
        Assert.Equal("/api/enrollment-exceptions/{id:guid}/cancel", Routes.EnrollmentExceptionCancelPattern);
        Assert.Equal("/api/groups/{id:guid}/members/remove", Routes.GroupMembersRemovePattern);
        Assert.Equal("/api/groups/{id:guid}/suggestions", Routes.GroupSuggestionsPattern);
        Assert.Equal("/api/transfers/preview", Routes.TransfersPreview);
        Assert.Equal("/api/students/{id:guid}/transfers", Routes.StudentTransfersPattern);
        Assert.Equal("/api/students/{id:guid}/enrollments", Routes.StudentEnrollmentsPattern);

        Assert.Equal($"/api/classes/{id}/students/{other}/remove", Routes.ClassStudentRemove(id, other));
        Assert.Equal($"/api/classes/matrix?orgUnitId={id}&yearId={other}", Routes.ClassesMatrixFor(id, other));
        Assert.Equal($"/api/class-courses/{id}/teachers/{other}/end", Routes.ClassCourseTeacherEnd(id, other));
        Assert.Equal($"/api/class-courses/{id}/groups", Routes.ClassCourseGroups(id));
        Assert.Equal($"/api/enrollment-exceptions/{id}/approve", Routes.EnrollmentExceptionApprove(id));
        Assert.Equal($"/api/groups/{id}/members", Routes.GroupMembers(id));
        Assert.Equal($"/api/students/{id}/enrollments", Routes.StudentEnrollments(id));

        StudentContractTests.AssertUniqueApiRoutes();
    }

    [Fact]
    public void Import_kinds_include_the_M3_kinds() =>
        Assert.Equal(["courses", "class-students"], ImportKinds.All.Intersect(["courses", "class-students"]));

    [Fact]
    public void Class_enrollment_and_plan_dtos_round_trip()
    {
        var teacher = new ClassCourseTeacherDto(Guid.NewGuid(), Guid.NewGuid(), "معلم", "Teacher", TeacherRoles.Primary,
            new DateOnly(2026, 9, 1), null, true);
        var classCourse = new ClassCourseDto(Guid.NewGuid(), Guid.NewGuid(), "١/أ", "1/A", Guid.NewGuid(), "MATH-1", "رياضيات", "Math",
            false, 30, [teacher], null);
        var result = new AddStudentsResultDto(3, 30, [new RestrictionSkipDto(Guid.NewGuid(), classCourse.Id, ["Religion"])], true);
        var plan = new ClassLessonPlanDto(classCourse.Id, 2, new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc), "Week",
            [new ClassSectionDto(Guid.NewGuid(), 1, "الأسبوع ١", "Week 1", false,
                [new ClassLessonDto(Guid.NewGuid(), 1, "الدرس", "Lesson", null, null, new DateOnly(2026, 9, 8), true)])]);
        var preview = new TransferPreviewDto(["MATH-1"], ["ART-1"], ["MUS-1"], [], false);

        Assert.Equal(teacher, RoundTrip(teacher));
        Assert.Null(RoundTrip(classCourse).LessonPlanVersion);
        Assert.Equal(["Religion"], RoundTrip(result).RestrictionSkips[0].FailedTypes);
        Assert.True(RoundTrip(plan).Sections[0].Lessons[0].IsHidden);
        Assert.Equal(["ART-1"], RoundTrip(preview).DroppedCourseCodes);
        Assert.Equal(EnrollmentStatuses.Active, new EnrollmentQuery(null, null).Status);
        Assert.Equal(ExceptionStatuses.Pending, new ExceptionQuery(null).Status);
        Assert.Equal(Paging.DefaultPageSize, new ClassQuery(null).PageSize);
        var bulk = new BulkCreateClassesRequest(Guid.NewGuid(), Guid.NewGuid(), "الصف {N}", "Grade {N}", "G{N}", 1, 5, 30);
        Assert.Equal(bulk, RoundTrip(bulk));
        Assert.Equal(1, RoundTrip(new StudentGroupDto(Guid.NewGuid(), classCourse.Id, "م", "G", "Gender", "Female", 1)).MemberCount);
    }
}
