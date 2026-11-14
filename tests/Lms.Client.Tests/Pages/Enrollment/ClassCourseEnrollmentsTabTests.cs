using Bunit;
using Lms.Client.Pages.Enrollment;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Pages.Enrollment;

public sealed class ClassCourseEnrollmentsTabTests : StudentsTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly FakeEnrollmentApi _enrollment = new();

    public ClassCourseEnrollmentsTabTests() => Services.AddSingleton<IEnrollmentApi>(_enrollment);

    private IRenderedComponent<ClassCourseEnrollmentsTab> RenderTab(bool required, bool manage = true)
    {
        RenderProviders();
        return Render<ClassCourseEnrollmentsTab>(p => p
            .Add(t => t.ClassCourseId, FakeEnrollmentApi.ClassCourseId).Add(t => t.ClassId, FakeClasses.Grade1A.Id)
            .Add(t => t.IsRequired, required).Add(t => t.CanManage, manage));
    }

    [Fact]
    public async Task Optional_course_enroll_and_drop()
    {
        var cut = RenderTab(required: false);
        // Ahmed (in the class, enrolled) is listed; Laila (in the class, not enrolled in this course) is the candidate.
        cut.WaitForElement("[data-testid=classcourse-enrollments-table]");
        var laila = _enrollment.ClassStudents[1].StudentId;
        await (await cut.WaitForElementAsync($"input[data-testid=enroll-pick-{laila}]", Wait)).ChangeAsync(new() { Value = true });

        await cut.Find("[data-testid=enroll-submit]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(_enrollment.Enrolls), Wait);
        Assert.Equal(FakeEnrollmentApi.ClassCourseId, _enrollment.Enrolls[0].ClassCourseId);
        Assert.Equal([laila], _enrollment.Enrolls[0].Request.StudentIds);

        var ahmed = _enrollment.ClassStudents[0].StudentId;
        _ = cut.Find($"[data-testid=drop-{ahmed}]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=reason-confirm]");
        Dialogs.Find("textarea[data-testid=reason-text]").Input("Not interested");
        await Dialogs.Find("[data-testid=reason-confirm]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(_enrollment.Drops), Wait);
        Assert.Equal("Not interested", _enrollment.Drops[0].Request.Reason);
    }

    [Fact]
    public async Task Required_course_has_nothing_to_enroll_or_drop_by_hand()
    {
        var cut = RenderTab(required: true);

        cut.WaitForElement("[data-testid=classcourse-enrollments-table]");
        Assert.Empty(cut.FindAll("[data-testid=enroll-candidates]"));
        Assert.Empty(cut.FindAll("[data-testid^=drop-]"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Without_Enrollment_Manage_the_optional_course_is_read_only()
    {
        var cut = RenderTab(required: false, manage: false);

        cut.WaitForElement("[data-testid=classcourse-enrollments-table]");
        Assert.Empty(cut.FindAll("[data-testid=enroll-submit]"));
        Assert.Empty(cut.FindAll("[data-testid^=drop-]"));
        await Task.CompletedTask;
    }
}
