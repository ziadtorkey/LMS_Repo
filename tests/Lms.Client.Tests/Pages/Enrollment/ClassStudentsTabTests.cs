using System.Globalization;
using Bunit;
using Lms.Client.Pages.Enrollment;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Lms.Shared.Enrollment;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Enrollment;

public sealed class ClassStudentsTabTests : StudentsTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly FakeEnrollmentApi _enrollment = new();
    private readonly List<RestrictionSkipDto> _requested = [];
    private IRenderedComponent<MudSnackbarProvider> _snackbars = default!;

    public ClassStudentsTabTests() => Services.AddSingleton<IEnrollmentApi>(_enrollment);

    private ClassSummaryDto Class => FakeClasses.Grade1A;

    private IRenderedComponent<ClassStudentsTab> RenderTab(bool manage = true, bool transfer = true, bool exception = true, ClassSummaryDto? cls = null)
    {
        RenderProviders();
        _snackbars = Render<MudSnackbarProvider>();
        return Render<ClassStudentsTab>(p => p
            .Add(t => t.Class, cls ?? Class)
            .Add(t => t.CanManage, manage).Add(t => t.CanTransfer, transfer).Add(t => t.CanRequestException, exception)
            .Add(t => t.OnRequestException, EventCallback.Factory.Create<RestrictionSkipDto>(this, _requested.Add)));
    }

    private Guid Unplaced => Students.All.First(s => s.Summary.CurrentClassId is null && s.Summary.Status == Lms.Shared.People.PersonStatuses.Active).Summary.Id;

    private async Task AddOneAsync(IRenderedComponent<ClassStudentsTab> cut)
    {
        _ = cut.WaitForElement("[data-testid=class-students-add]").ClickAsync(new());
        await (await Dialogs.WaitForElementAsync($"input[data-testid=add-student-{Unplaced}]")).ChangeAsync(new() { Value = true });
        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());
    }

    [Fact]
    public async Task Tab_lists_class_students_with_status()
    {
        Culture.Current = new CultureInfo("en");
        var cut = RenderTab();

        cut.WaitForElement("[data-testid=class-students-table]");
        Assert.Contains("Ahmed Saeed", cut.Markup);
        Assert.Contains("In the class", cut.Find($"[data-testid=class-student-status-{_enrollment.ClassStudents[0].Id}]").TextContent);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Add_students_reports_restriction_skips_with_request_exception_link()
    {
        Culture.Current = new CultureInfo("en");
        var student = Unplaced;
        _enrollment.AddResult = new AddStudentsResultDto(1, 3, [new RestrictionSkipDto(student, FakeEnrollmentApi.ClassCourseId, [Lms.Shared.Courses.CourseRestrictionTypes.Gender])], false);
        var cut = RenderTab();
        await AddOneAsync(cut);

        var panel = await cut.WaitForElementAsync("[data-testid=skips-panel]", Wait);
        Assert.Contains("Gender", panel.TextContent);
        Assert.Contains("1 students added, 3 course enrollments created.", cut.Find("[data-testid=add-summary]").TextContent);
        Assert.Equal([student], _enrollment.Adds[0].Request.StudentIds);

        await cut.Find($"[data-testid=skip-request-{student}-{FakeEnrollmentApi.ClassCourseId}]").ClickAsync(new());

        Assert.Equal([new RestrictionSkipDto(student, FakeEnrollmentApi.ClassCourseId, ["Gender"])], _requested, new SkipComparer());
    }

    [Fact]
    public async Task Request_exception_link_hidden_without_Enrollment_RequestException()
    {
        var student = Unplaced;
        _enrollment.AddResult = new AddStudentsResultDto(1, 3, [new RestrictionSkipDto(student, FakeEnrollmentApi.ClassCourseId, ["Gender"])], false);
        var cut = RenderTab(exception: false);
        await AddOneAsync(cut);

        await cut.WaitForElementAsync("[data-testid=skips-panel]", Wait);
        Assert.Empty(cut.FindAll($"[data-testid=skip-request-{student}-{FakeEnrollmentApi.ClassCourseId}]"));
    }

    [Fact]
    public async Task Over_capacity_asks_for_confirmation()
    {
        Culture.Current = new CultureInfo("en");
        _enrollment.Failure = new ApiProblemException(409, "over_capacity", null, null);
        var cut = RenderTab();
        _ = cut.WaitForElement("[data-testid=class-students-add]").ClickAsync(new());
        await (await Dialogs.WaitForElementAsync($"input[data-testid=add-student-{Unplaced}]")).ChangeAsync(new() { Value = true });

        _ = Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        var yes = await Dialogs.WaitForElementAsync("button.mud-message-box__yes-button", Wait);
        Assert.Single(_enrollment.Adds);
        Assert.False(_enrollment.Adds[0].Request.ConfirmOverCapacity);
        await yes.ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Equal(2, _enrollment.Adds.Count), Wait);
        Assert.True(_enrollment.Adds[1].Request.ConfirmOverCapacity);
        await cut.WaitForElementAsync("[data-testid=add-summary]", Wait);
    }

    [Fact]
    public async Task Class_full_shows_the_localized_error_and_keeps_the_dialog_open()
    {
        Culture.Current = new CultureInfo("en");
        _enrollment.Failure = new ApiProblemException(409, "class_full", null, null);
        var cut = RenderTab();
        await AddOneAsync(cut);

        Assert.Contains("The class is full.", (await Dialogs.WaitForElementAsync("[data-testid=dialog-error]", Wait)).TextContent);
        Assert.Empty(cut.FindAll("[data-testid=add-summary]"));
    }

    [Fact]
    public async Task Remove_asks_for_a_reason_and_sends_it()
    {
        var student = _enrollment.ClassStudents[0];
        var cut = RenderTab();
        _ = cut.WaitForElement($"[data-testid=class-student-remove-{student.Id}]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=reason-confirm]");

        await Dialogs.Find("[data-testid=reason-confirm]").ClickAsync(new()); // no reason yet
        Assert.Empty(_enrollment.Removals);

        Dialogs.Find("textarea[data-testid=reason-text]").Input("Moved to another school");
        await Dialogs.Find("[data-testid=reason-confirm]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(_enrollment.Removals), Wait);
        Assert.Equal((Class.Id, student.Id, "Moved to another school"), (_enrollment.Removals[0].ClassId, _enrollment.Removals[0].ClassStudentId, _enrollment.Removals[0].Request.Reason));
    }

    [Fact]
    public async Task Actions_are_hidden_without_manage_and_in_a_closed_class()
    {
        var student = _enrollment.ClassStudents[0];
        var viewOnly = RenderTab(manage: false, transfer: false);
        viewOnly.WaitForElement("[data-testid=class-students-table]");
        Assert.Empty(viewOnly.FindAll("[data-testid=class-students-add]"));
        Assert.Empty(viewOnly.FindAll($"[data-testid=class-student-remove-{student.Id}]"));
        Assert.Empty(viewOnly.FindAll($"[data-testid=class-student-transfer-{student.Id}]"));

        var closed = Render<ClassStudentsTab>(p => p.Add(t => t.Class, Class with { IsReadOnly = true }).Add(t => t.CanManage, true).Add(t => t.CanTransfer, true));
        closed.WaitForElement("[data-testid=class-students-table]");
        Assert.Empty(closed.FindAll("[data-testid=class-students-add]"));
        Assert.Empty(closed.FindAll($"[data-testid=class-student-remove-{student.Id}]"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Page_subtitle_has_no_lone_separator_when_the_year_name_is_empty()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Classes.View);
        RenderProviders();
        var page = Render<ClassStudentsPage>(p => p.Add(x => x.ClassId, Class.Id));

        var subtitle = page.WaitForElement("[data-testid=class-subtitle]").TextContent.Trim();

        Assert.Equal("Main School", subtitle); // the fake class has no year name: no leading "·"
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Tab_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        RenderProviders();
        var rtl = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true)
            .AddChildContent<ClassStudentsTab>(c => c.Add(t => t.Class, Class).Add(t => t.CanManage, true)));

        var table = rtl.WaitForElement("[data-testid=class-students-table]");
        Assert.Contains("أحمد سعيد", table.TextContent);
        Assert.Contains("في الفصل", table.TextContent);
        Assert.Contains("إضافة طلاب", rtl.Find("[data-testid=class-students-add]").TextContent);
        Assert.Equal("ltr", rtl.Find("[data-testid=class-students-table] td span").GetAttribute("dir"));
        await Task.CompletedTask;
    }

    private sealed class SkipComparer : IEqualityComparer<RestrictionSkipDto>
    {
        public bool Equals(RestrictionSkipDto? x, RestrictionSkipDto? y) =>
            x is not null && y is not null && x.StudentId == y.StudentId && x.ClassCourseId == y.ClassCourseId && x.FailedTypes.SequenceEqual(y.FailedTypes);

        public int GetHashCode(RestrictionSkipDto obj) => obj.StudentId.GetHashCode();
    }
}
