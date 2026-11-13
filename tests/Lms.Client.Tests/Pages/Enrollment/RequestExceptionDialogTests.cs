using System.Globalization;
using Bunit;
using Lms.Client.Pages.Enrollment;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Enrollment;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Enrollment;

public sealed class RequestExceptionDialogTests : StudentsTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly FakeEnrollmentApi _enrollment = new();

    public RequestExceptionDialogTests() => Services.AddSingleton<IEnrollmentApi>(_enrollment);

    private Guid Unplaced => Students.All.First(s => s.Summary.CurrentClassId is null && s.Summary.Status == Lms.Shared.People.PersonStatuses.Active).Summary.Id;

    /// <summary>The user's path: add a student to the class, see the restriction skip, ask for an exception from the skip row.</summary>
    private async Task<IRenderedComponent<ClassStudentsPage>> OpenFromSkipAsync()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Classes.View, Permissions.Enrollment.Manage, Permissions.Enrollment.RequestException);
        RenderProviders();
        Render<MudSnackbarProvider>();
        var student = Unplaced;
        _enrollment.AddResult = new AddStudentsResultDto(1, 0, [new RestrictionSkipDto(student, FakeEnrollmentApi.ClassCourseId, ["Gender", "Religion"])], false);
        var page = Render<ClassStudentsPage>(p => p.Add(x => x.ClassId, FakeClasses.Grade1A.Id));
        _ = page.WaitForElement("[data-testid=class-students-add]").ClickAsync(new());
        await (await Dialogs.WaitForElementAsync($"input[data-testid=add-student-{student}]")).ChangeAsync(new() { Value = true });
        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());
        _ = (await page.WaitForElementAsync($"[data-testid=skip-request-{student}-{FakeEnrollmentApi.ClassCourseId}]", Wait)).ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=exception-reason]");
        return page;
    }

    [Fact]
    public async Task Dialog_from_a_skip_shows_the_student_and_what_failed()
    {
        await OpenFromSkipAsync();

        Assert.Contains(Students.All.First(s => s.Summary.Id == Unplaced).Summary.StudentCode, Dialogs.Find("[data-testid=exception-student]").TextContent);
        Assert.Contains("Gender", Dialogs.Markup);
        Assert.Contains("Religion", Dialogs.Markup);
    }

    [Fact]
    public async Task Reason_is_required_and_the_request_goes_to_the_class_course()
    {
        var page = await OpenFromSkipAsync();

        await Dialogs.Find("[data-testid=exception-send]").ClickAsync(new());
        Assert.Empty(_enrollment.ExceptionRequests);

        Dialogs.Find("textarea[data-testid=exception-reason]").Input("The student studied it abroad");
        await Dialogs.Find("[data-testid=exception-send]").ClickAsync(new());

        await page.WaitForAssertionAsync(() => Assert.Single(_enrollment.ExceptionRequests), Wait);
        Assert.Equal((FakeEnrollmentApi.ClassCourseId, Unplaced, "The student studied it abroad"),
            (_enrollment.ExceptionRequests[0].ClassCourseId, _enrollment.ExceptionRequests[0].Request.StudentId, _enrollment.ExceptionRequests[0].Request.Reason));
    }

    [Fact]
    public async Task Server_problem_is_shown_and_the_dialog_stays_open()
    {
        await OpenFromSkipAsync();
        _enrollment.Failure = new ApiProblemException(409, "restriction_not_met", null, null);
        Dialogs.Find("textarea[data-testid=exception-reason]").Input("Because");

        await Dialogs.Find("[data-testid=exception-send]").ClickAsync(new());

        Assert.NotEmpty((await Dialogs.WaitForElementAsync("[data-testid=dialog-error]", Wait)).TextContent);
    }
}
