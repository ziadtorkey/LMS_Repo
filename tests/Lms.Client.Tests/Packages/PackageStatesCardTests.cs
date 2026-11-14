using System.Globalization;
using Bunit;
using Lms.Client.Components.Packages;
using Lms.Client.Services.Api;
using Lms.Client.Services.Packages;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Lms.Shared.Packages;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Packages;

/// <summary>Package states on Home and the course home (T18): in progress, completed, and the D57 wording of the action.</summary>
public sealed class PackageStatesCardTests : LessonPlansTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeLessonPlansApi.ArabicClassCourseId;
    private static readonly Guid Lesson = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    private readonly FakeModulesApi _modules = new() { AsStudent = true };
    private readonly FakePackagePlayerApi _player = new();

    public PackageStatesCardTests()
    {
        Services.AddSingleton<IModulesApi>(_modules);
        Services.AddSingleton<IPackagePlayerApi>(_player);
        Culture.Current = new CultureInfo("en");
    }

    private static ModuleNodeDto Node(string kind, string title, string? state, bool visible = true) =>
        new(Guid.NewGuid(), kind, 1, "ع " + title, title, ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, null, null,
            CompletionRules.Submit, visible, state);

    /// <summary>Puts the nodes into the first lesson of the plan and clears the demo modules of the other lessons.</summary>
    private void Publish(params ModuleNodeDto[] nodes)
    {
        var plan = Plans.Plans[Cc];
        Plans.Plans[Cc] = plan with
        {
            Sections = plan.Sections.Select(s => s with { Lessons = s.Lessons.Select(l => l.Key == Lesson ? l with { Modules = nodes } : l with { Modules = [] }).ToList() }).ToList(),
        };
    }

    private async Task<IRenderedComponent<PackageStatesCard>> RenderCardAsync(bool courseHome = true)
    {
        await GrantAsync(Permissions.Modules.View, Permissions.LessonPlans.View);
        RenderProviders();
        return Render<PackageStatesCard>(p => { if (courseHome) p.Add(x => x.ClassCourseId, Cc); });
    }

    /// <summary>A completed package module whose package has this grading method.</summary>
    private ModuleNodeDto Completed(string title, string method)
    {
        var seeded = _modules.Seed(Cc, Lesson, new CreateModuleRequest(ModuleKinds.Scorm, "ع", title, ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null,
            CompletionRules.Submit, null, new ModuleContentInput()));
        var node = seeded.Node with { CompletionState = CompletionStates.Completed };
        _modules.RefIds[node.Id] = Guid.NewGuid(); // the package the module points to
        _player.MyState = new PackageStudentStateDto(method, null, true, 85);
        return node;
    }

    [Fact]
    public async Task An_unfinished_package_offers_Resume_and_links_to_its_module()
    {
        var node = Node(ModuleKinds.Scorm, "Fractions lab", CompletionStates.InProgress);
        Publish(node);

        var card = await RenderCardAsync();
        card.WaitForElement("[data-testid=home-packages]", Wait);

        Assert.Equal("Resume", card.Find($"[data-testid=home-package-action-{node.Id}]").TextContent.Trim());
        Assert.Equal("In progress", card.Find($"[data-testid=home-package-state-{node.Id}]").TextContent.Trim());
        Assert.EndsWith($"class-courses/{Cc}/modules/{node.Id}", card.Find($"[data-testid=home-package-action-{node.Id}]").GetAttribute("href"));
    }

    [Theory]
    [InlineData(PackageGradingMethods.Highest, "Try again")]
    [InlineData(PackageGradingMethods.Last, "Try again")]
    [InlineData(PackageGradingMethods.First, "Review (not graded)")]
    public async Task A_completed_package_uses_the_D57_wording_of_its_grading_method(string method, string expected)
    {
        var node = Completed("Fractions lab", method);
        Publish(node);

        var card = await RenderCardAsync();
        card.WaitForElement("[data-testid=home-packages]", Wait);

        Assert.Equal(expected, card.Find($"[data-testid=home-package-action-{node.Id}]").TextContent.Trim());
        Assert.Equal(method, card.Find($"[data-testid=home-package-action-{node.Id}]").GetAttribute("data-method"));
        Assert.Equal("Completed", card.Find($"[data-testid=home-package-state-{node.Id}]").TextContent.Trim());
    }

    [Fact]
    public async Task Not_started_hidden_and_non_package_modules_are_left_out_and_no_card_shows_when_nothing_is_left()
    {
        Publish(Node(ModuleKinds.Scorm, "Not started", CompletionStates.NotStarted), Node(ModuleKinds.H5p, "Hidden", CompletionStates.InProgress, visible: false),
            Node(ModuleKinds.Page, "A page", CompletionStates.Completed));

        var card = await RenderCardAsync();
        await Task.Delay(100);

        Assert.Empty(card.FindAll("[data-testid=home-packages]"));
    }

    [Fact]
    public async Task Unfinished_activities_come_first_and_there_are_at_most_four()
    {
        var done = Completed("Done", PackageGradingMethods.Highest);
        var nodes = new[] { done }.Concat(Enumerable.Range(1, 5).Select(i => Node(i % 2 == 0 ? ModuleKinds.H5p : ModuleKinds.Scorm, $"Open {i}", CompletionStates.InProgress))).ToArray();
        Publish(nodes);

        var card = await RenderCardAsync();
        card.WaitForElement("[data-testid=home-packages]", Wait);

        var rows = card.FindAll("[data-testid^=home-package-row-]");
        Assert.Equal(4, rows.Count);
        Assert.DoesNotContain(rows, r => r.GetAttribute("data-testid")!.EndsWith(done.Id.ToString())); // five unfinished fill the four rows, the completed one waits
    }

    [Fact]
    public async Task On_Home_the_row_names_its_course_and_on_the_course_home_it_does_not()
    {
        var node = Node(ModuleKinds.Scorm, "Fractions lab", CompletionStates.InProgress);
        Publish(node);
        var home = await RenderCardAsync(courseHome: false);
        home.WaitForElement("[data-testid=home-packages]", Wait);
        var course = Plans.Mine.First(c => c.Id == Cc);
        Assert.Contains(course.CourseNameEn, home.Find($"[data-testid=home-package-row-{node.Id}]").TextContent);
        await DisposeComponentsAsync();

        var courseHome = await RenderCardAsync(courseHome: true);
        courseHome.WaitForElement("[data-testid=home-packages]", Wait);
        Assert.DoesNotContain(course.CourseNameEn, courseHome.Find($"[data-testid=home-package-row-{node.Id}]").TextContent);
    }

    [Fact]
    public async Task The_card_is_in_arabic_too()
    {
        Culture.Current = new CultureInfo("ar");
        var node = Completed("Fractions lab", PackageGradingMethods.First);
        Publish(node);

        var card = await RenderCardAsync();
        card.WaitForElement("[data-testid=home-packages]", Wait);

        Assert.Equal("مراجعة (غير محتسبة)", card.Find($"[data-testid=home-package-action-{node.Id}]").TextContent.Trim());
        Assert.Equal("مكتمل", card.Find($"[data-testid=home-package-state-{node.Id}]").TextContent.Trim());
        Assert.Equal("الأنشطة التفاعلية", card.Find("[data-testid=home-packages] h2, [data-testid=home-packages] .lms-section-title, [data-testid=home-packages] h3").TextContent.Trim());
    }
}
