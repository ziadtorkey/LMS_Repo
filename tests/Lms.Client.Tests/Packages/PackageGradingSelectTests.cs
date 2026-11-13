using System.Globalization;
using Bunit;
using Lms.Client.Components.Packages;
using Lms.Client.Services.Api;
using Lms.Client.Services.Packages;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.Packages;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Packages;

/// <summary>D57: the teacher picks per package module which attempt is graded (Highest by default, Last, First); design spec M4b §4.1.</summary>
public sealed class PackageGradingSelectTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly Guid _lesson = Guid.NewGuid();

    public PackageGradingSelectTests() => Culture.Current = new CultureInfo("en");

    private PackageUploadDto Ready() =>
        new(Guid.NewGuid(), Guid.NewGuid(), _lesson, "lesson.zip", PackageKinds.Scorm, PackageUploadStatuses.Ready, null, "Lesson", "2004", null, null, DateTime.UtcNow);

    private IRenderedComponent<PackageAttachForm> Open(List<AttachPackageModuleRequest> sent)
    {
        Render<MudPopoverProvider>();
        return Render<PackageAttachForm>(p => p
            .Add(x => x.Package, Ready())
            .Add(x => x.Lessons, [])
            .Add(x => x.OnAttach, request => { sent.Add(request); return Task.FromResult<string?>(null); }));
    }

    private static async Task UseRuleAsync(IRenderedComponent<PackageAttachForm> cut, string rule)
    {
        var select = cut.FindComponents<MudSelect<string>>().Single(s => s.Markup.Contains("package-rule"));
        await cut.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(rule));
    }

    [Fact]
    public async Task Highest_is_preselected_and_sent_by_default()
    {
        var sent = new List<AttachPackageModuleRequest>();
        var cut = Open(sent);
        await UseRuleAsync(cut, CompletionRules.Score);

        Assert.NotNull(cut.Find("[data-testid=package-grading-Highest].lms-choice-on"));
        cut.Find("[data-testid=package-attach]").Click();

        cut.WaitForAssertion(() => Assert.Equal(PackageGradingMethods.Highest, Assert.Single(sent).GradingMethod), Wait);
    }

    [Theory]
    [InlineData(PackageGradingMethods.Last)]
    [InlineData(PackageGradingMethods.First)]
    [InlineData(PackageGradingMethods.Highest)]
    public async Task The_chosen_method_is_sent_with_the_attach_request(string method)
    {
        var sent = new List<AttachPackageModuleRequest>();
        var cut = Open(sent);
        await UseRuleAsync(cut, CompletionRules.Score);

        cut.Find($"[data-testid=package-grading-{method}] input").Change(true);
        cut.Find("[data-testid=package-attach]").Click();

        cut.WaitForAssertion(() => Assert.Equal(method, Assert.Single(sent).GradingMethod), Wait);
    }

    [Fact]
    public void The_group_shows_only_for_the_Score_rule_and_other_rules_send_the_default()
    {
        var sent = new List<AttachPackageModuleRequest>();
        var cut = Open(sent);   // the default rule is Submit

        Assert.Empty(cut.FindAll("[data-testid=package-grading]"));
        cut.Find("[data-testid=package-attach]").Click();

        cut.WaitForAssertion(() => Assert.Equal(PackageGradingMethods.Highest, Assert.Single(sent).GradingMethod), Wait);
    }

    [Fact]
    public async Task Switching_back_from_Score_does_not_send_a_hidden_choice()
    {
        var sent = new List<AttachPackageModuleRequest>();
        var cut = Open(sent);
        await UseRuleAsync(cut, CompletionRules.Score);
        cut.Find($"[data-testid=package-grading-{PackageGradingMethods.First}] input").Change(true);
        await UseRuleAsync(cut, CompletionRules.Submit);

        cut.Find("[data-testid=package-attach]").Click();

        cut.WaitForAssertion(() => Assert.Equal(PackageGradingMethods.Highest, Assert.Single(sent).GradingMethod), Wait);
    }

    [Theory]
    [InlineData("en", "When a student opens it again", "Keep the highest score", "Use the latest score", "Keep the first score")]
    [InlineData("ar", "عند فتح الطالب له مرة أخرى", "احتساب أعلى درجة", "احتساب آخر درجة", "احتساب الدرجة الأولى")]
    public async Task The_group_and_its_options_use_the_designers_wording(string culture, string legend, string highest, string last, string first)
    {
        Culture.Current = new CultureInfo(culture);
        var cut = Open([]);
        await UseRuleAsync(cut, CompletionRules.Score);

        var group = cut.Find("[data-testid=package-grading]");

        Assert.Contains(legend, group.TextContent);
        Assert.Contains(highest, group.TextContent);
        Assert.Contains(last, group.TextContent);
        Assert.Contains(first, group.TextContent);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Every_option_has_its_own_short_caption(string culture)
    {
        Culture.Current = new CultureInfo(culture);
        var cut = Open([]);
        await UseRuleAsync(cut, CompletionRules.Score);

        var captions = PackageGradingMethods.All
            .Select(m => cut.Find($"[data-testid=package-grading-{m}] .lms-muted").TextContent.Trim()).ToList();

        Assert.All(captions, c => { Assert.NotEmpty(c); Assert.DoesNotContain("Package_Grading", c); });   // a missing key would show its name
        Assert.Equal(PackageGradingMethods.All.Count, captions.Distinct().Count());
    }

    [Fact]
    public async Task The_fake_rejects_a_method_the_contract_does_not_list()
    {
        var packages = new FakePackagesApi();
        var settings = new ModuleSettingsInput("a", "a", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.Submit, null);

        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => packages.AttachAsync(Guid.NewGuid(), new AttachPackageModuleRequest(settings, "Average")));

        Assert.Equal(ProblemCodes.validation_failed, ex.Code);
    }
}
