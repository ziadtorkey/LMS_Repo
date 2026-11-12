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
}
