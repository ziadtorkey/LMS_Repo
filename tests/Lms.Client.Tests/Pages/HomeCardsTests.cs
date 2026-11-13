using System.Globalization;
using Bunit;
using Lms.Client.Pages;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Pages;

public sealed class HomeCardsTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;
    private readonly FakeModulesApi _modules = new() { AsStudent = true };

    public HomeCardsTests()
    {
        Services.AddSingleton<IModulesApi>(_modules);
        Culture.Current = new CultureInfo("en");
    }

    private async Task<IRenderedComponent<Home>> OpenAsync(params string[] permissions)
    {
        await GrantAsync(permissions);
        RenderProviders();
        var page = Render<Home>();
        page.WaitForElement("p, .mud-typography-body1", Wait);
        return page;
    }

    private ModuleDetailDto SeedManual() =>
        _modules.Seed(Cc, Guid.NewGuid(), new CreateModuleRequest(ModuleKinds.Page, "ع", "Read me", ModuleVisibilities.Published, null, null,
            ModuleTargets.ClassCourse, null, CompletionRules.Manual, null, new ModuleContentInput(BodyHtmlEn: "<p>x</p>")));

    [Fact]
    public async Task Home_cards_depend_on_permissions_student_gets_continue_learning()
    {
        var next = SeedManual();

        var page = await OpenAsync(Permissions.Modules.View, Permissions.LessonPlans.View);

        page.WaitForElement("[data-testid=home-continue-learning]", Wait);
        Assert.Contains($"class-courses/{Cc}/modules/{next.Node.Id}", page.Find($"[data-testid=home-learn-continue-{Cc}]").GetAttribute("href"));
        Assert.Contains($"class-courses/{Cc}/learn", page.Find($"[data-testid=home-learn-open-{Cc}]").GetAttribute("href"));
        Assert.Contains("0 of 1", page.Find($"[data-testid=home-learn-{Cc}]").TextContent);
    }

    [Fact]
    public async Task Home_has_one_h1_title_with_the_welcome_as_its_subtitle()
    {
        var page = await OpenAsync(Permissions.LessonPlans.View);

        page.WaitForElement("[data-testid=home-title]", Wait);
        Assert.Single(page.FindAll("h1"));
        Assert.Contains("Welcome", page.Markup);
    }

    [Fact]
    public async Task Home_keeps_one_filled_continue_button_and_the_others_are_secondary()
    {
        SeedManual();
        var page = await OpenAsync(Permissions.Modules.View, Permissions.LessonPlans.View);
        page.WaitForElement("[data-testid=home-continue-learning]", Wait);

        var continues = page.FindAll("[data-testid^=home-learn-continue-]");

        Assert.NotEmpty(continues);
        Assert.Single(continues.Where(b => b.ClassList.Contains("mud-button-filled")).ToList());
        Assert.All(continues.Skip(1), b => Assert.Contains("mud-button-outlined", b.ClassList));
    }

    [Fact]
    public async Task Home_cards_depend_on_permissions_teacher_gets_no_student_cards()
    {
        SeedManual();

        var page = await OpenAsync(Permissions.Modules.View, Permissions.Modules.Manage, Permissions.LessonPlans.View);

        page.WaitForElement("[data-testid=home-my-class-courses]", Wait);
        Assert.Empty(page.FindAll("[data-testid=home-continue-learning]"));
    }

    [Fact]
    public async Task Home_cards_depend_on_permissions_no_permissions_no_cards()
    {
        var page = await OpenAsync();

        Assert.Empty(page.FindAll("[data-testid=home-continue-learning]"));
        Assert.Empty(page.FindAll("[data-testid=home-my-class-courses]"));
    }

    private sealed class PlansWithoutView : ILessonPlansApi
    {
        public Task<IReadOnlyList<Lms.Shared.Classes.ClassCourseDto>> GetMyClassCoursesAsync(CancellationToken ct = default) =>
            Task.FromException<IReadOnlyList<Lms.Shared.Classes.ClassCourseDto>>(new Lms.Client.Services.ApiForbiddenException());
        public Task<Lms.Shared.Classes.ClassLessonPlanDto> GetPlanAsync(Guid classCourseId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_role_with_Modules_View_but_without_LessonPlans_View_is_not_sent_to_the_forbidden_page()
    {
        Services.AddSingleton<ILessonPlansApi>(new PlansWithoutView());
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        var before = navigation.Uri;

        var page = await OpenAsync(Permissions.Modules.View);

        page.WaitForElement("p, .mud-typography-body1", Wait);
        Assert.Equal(before, navigation.Uri);
        Assert.Empty(page.FindAll("[data-testid=home-continue-learning]")); // nothing to show, and no redirect
    }
}
