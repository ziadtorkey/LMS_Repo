using System.Globalization;
using Bunit;
using Lms.Client.Pages.Gradebook;
using Lms.Client.Services;
using Lms.Client.Services.Grading;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Gradebook;

/// <summary>The phone gradebook: one student at a time, in one layout (L19, M5 Task 24).</summary>
public sealed class StudentFirstListTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly FakeGradebookApi _api = new();

    public StudentFirstListTests()
    {
        Culture.Current = new CultureInfo("en");
        Services.AddSingleton<IGradebookApi>(_api);
        Services.AddSingleton<IViewport>(new FakeViewport { IsPhone = true });
    }

    private async Task<IRenderedComponent<GradebookPage>> OpenAsync()
    {
        await GrantAsync(Permissions.Grades.View, Permissions.Grades.Manage);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        Render<MudPopoverProvider>();
        Render<MudDialogProvider>();
        var page = Render<GradebookPage>(p => p.Add(x => x.Id, FakeGradebookApi.DemoClassCourseId));
        page.WaitForElement("[data-testid=student-first-list]", Wait);
        return page;
    }

    [Fact]
    public async Task It_starts_on_the_first_student_with_their_position()
    {
        var page = await OpenAsync();

        Assert.Equal("Sara Ahmed", page.Find("[data-testid=student-name]").TextContent.Trim());
        Assert.Equal("1 of 3", page.Find("[data-testid=student-position]").TextContent.Trim());
        Assert.True(page.Find("[data-testid=student-prev]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Next_and_previous_move_between_students_and_stop_at_the_ends()
    {
        var page = await OpenAsync();

        page.Find("[data-testid=student-next]").Click();
        Assert.Equal("Khaled Ali", page.Find("[data-testid=student-name]").TextContent.Trim());
        page.Find("[data-testid=student-next]").Click();
        Assert.Equal("3 of 3", page.Find("[data-testid=student-position]").TextContent.Trim());
        Assert.True(page.Find("[data-testid=student-next]").HasAttribute("disabled"));
        page.Find("[data-testid=student-prev]").Click();

        Assert.Equal("Khaled Ali", page.Find("[data-testid=student-name]").TextContent.Trim());
    }

    [Fact]
    public async Task Items_are_rows_grouped_by_category_with_the_final_and_band_in_the_footer()
    {
        var page = await OpenAsync();

        Assert.NotNull(page.Find($"[data-testid=phone-item-{FakeGradebookApi.ManualItemId}]"));
        Assert.Contains("out of 20", page.Find($"[data-testid=phone-item-{FakeGradebookApi.ManualItemId}]").TextContent);
        Assert.Equal("event", page.Find($"[data-testid=phone-item-{FakeGradebookApi.AutoItemId}]").GetAttribute("data-state")!.ToLowerInvariant());
        Assert.NotNull(page.Find("[data-testid=phone-final]"));
        Assert.NotNull(page.Find("[data-testid=phone-band]"));
    }

    [Fact]
    public async Task A_note_says_the_phone_is_for_one_student()
    {
        var page = await OpenAsync();

        Assert.Contains("larger screen", page.Find("[data-testid=phone-note]").TextContent);
    }

    [Fact]
    public async Task Search_narrows_the_list_and_none_says_so()
    {
        var page = await OpenAsync();

        page.Find("[data-testid=student-search]").Input("khal");
        Assert.Equal("Khaled Ali", page.Find("[data-testid=student-name]").TextContent.Trim());
        Assert.Equal("1 of 1", page.Find("[data-testid=student-position]").TextContent.Trim());

        page.Find("[data-testid=student-search]").Input("zzz");
        Assert.NotNull(page.Find("[data-testid=student-search-none]"));
        Assert.Empty(page.FindAll("[data-testid=student-name]"));
    }

    [Fact]
    public async Task Search_matches_the_student_number_as_well()
    {
        var page = await OpenAsync();

        page.Find("[data-testid=student-search]").Input("S-0003");

        Assert.Equal("Layla Hassan", page.Find("[data-testid=student-name]").TextContent.Trim());
    }

    [Fact]
    public async Task The_preview_button_opens_what_the_student_sees()
    {
        var page = await OpenAsync();

        page.Find("[data-testid=phone-preview]").Click();

        page.WaitForElement("[data-testid=student-drawer]", Wait);
        page.WaitForAssertion(() => Assert.Equal(1, _api.StudentViewCalls), Wait);
    }

    [Fact]
    public async Task Arabic_phone_list_uses_localized_labels()
    {
        Culture.Current = new CultureInfo("ar");

        var page = await OpenAsync();

        Assert.Equal("سارة أحمد", page.Find("[data-testid=student-name]").TextContent.Trim());
        Assert.Contains("1 من 3", page.Find("[data-testid=student-position]").TextContent);
        Assert.DoesNotContain("Grd_", page.Markup);
    }
}
