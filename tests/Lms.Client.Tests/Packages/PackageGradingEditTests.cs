using System.Globalization;
using Bunit;
using Lms.Client.Components.Content;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Services.Packages;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Lms.Shared.Packages;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Packages;

/// <summary>D57: staff see and change the grading method of an attached package module in the module edit dialog (the PUT of #549).</summary>
public sealed class PackageGradingEditTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = Guid.NewGuid(), Lesson = Guid.NewGuid();

    private readonly FakeModulesApi _modules = new();
    private readonly FakePackagesApi _packages = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public PackageGradingEditTests()
    {
        Services.AddSingleton<IModulesApi>(_modules);
        Services.AddSingleton<IAssetsApi, FakeAssetsApi>();
        Services.AddSingleton<IPackagesApi>(_packages);
        Culture.Current = new CultureInfo("en");
    }

    private async Task StartAsync()
    {
        await GrantAsync(Permissions.Modules.Manage);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
    }

    /// <summary>An attached SCORM module (its package id is the module's RefId) with the given rule and grading method.</summary>
    private (ModuleDetailDto Module, Guid PackageId) PackageModule(string rule, string method)
    {
        var packageId = Guid.NewGuid();
        _packages.SeedAttached(packageId, Cc, Lesson, PackageKinds.Scorm, method);
        var detail = _modules.Seed(Cc, Lesson, new CreateModuleRequest(ModuleKinds.Scorm, "حزمة", "Fractions", ModuleVisibilities.Published, null, null,
            ModuleTargets.ClassCourse, null, rule, null, new ModuleContentInput()), refId: packageId);
        return (detail, packageId);
    }

    private void OpenDialog(string kind, ModuleDetailDto existing) =>
        _ = Services.GetRequiredService<IDialogService>().ShowAsync<ModuleDialog>("t", new DialogParameters<ModuleDialog>
        {
            { x => x.ClassCourseId, Cc }, { x => x.LessonKey, Lesson }, { x => x.Kind, kind }, { x => x.Existing, existing },
        });

    [Fact]
    public async Task The_current_grading_method_is_preselected_for_a_Score_module()
    {
        var (module, _) = PackageModule(CompletionRules.Score, PackageGradingMethods.Last);
        await StartAsync();

        OpenDialog(ModuleKinds.Scorm, module);

        _dialogs.WaitForElement("[data-testid=package-grading]", Wait);
        Assert.NotNull(_dialogs.Find("[data-testid=package-grading-Last].lms-choice-on"));
    }

    [Fact]
    public async Task Changing_the_method_and_saving_sends_the_put()
    {
        var (module, packageId) = PackageModule(CompletionRules.Score, PackageGradingMethods.Highest);
        await StartAsync();
        OpenDialog(ModuleKinds.Scorm, module);
        _dialogs.WaitForElement("[data-testid=package-grading]", Wait);

        _dialogs.Find($"[data-testid=package-grading-{PackageGradingMethods.First}] input").Change(true);
        _dialogs.Find("[data-testid=module-save]").Click();

        _dialogs.WaitForAssertion(() => Assert.Equal((packageId, PackageGradingMethods.First), Assert.Single(_packages.GradingUpdates)), Wait);
    }

    [Fact]
    public async Task Changing_the_method_shows_the_recalculation_note_and_going_back_hides_it()
    {
        var (module, _) = PackageModule(CompletionRules.Score, PackageGradingMethods.Highest);
        await StartAsync();
        OpenDialog(ModuleKinds.Scorm, module);
        _dialogs.WaitForElement("[data-testid=package-grading]", Wait);
        Assert.Empty(_dialogs.FindAll("[data-testid=package-grading-note]"));   // nothing changed yet

        _dialogs.Find($"[data-testid=package-grading-{PackageGradingMethods.Last}] input").Change(true);

        var note = _dialogs.WaitForElement("[data-testid=package-grading-note]", Wait);
        Assert.Contains("Students' graded scores will be recalculated with the new rule.", note.TextContent);
        Assert.Contains("mud-alert-outlined-info", note.ClassName);

        _dialogs.Find($"[data-testid=package-grading-{PackageGradingMethods.Highest}] input").Change(true);
        _dialogs.WaitForAssertion(() => Assert.Empty(_dialogs.FindAll("[data-testid=package-grading-note]")), Wait);
    }

    [Fact]
    public async Task The_recalculation_note_is_in_arabic_too()
    {
        Culture.Current = new CultureInfo("ar");
        var (module, _) = PackageModule(CompletionRules.Score, PackageGradingMethods.Highest);
        await StartAsync();
        OpenDialog(ModuleKinds.Scorm, module);
        _dialogs.WaitForElement("[data-testid=package-grading]", Wait);

        _dialogs.Find($"[data-testid=package-grading-{PackageGradingMethods.First}] input").Change(true);

        Assert.Contains("ستُعاد حساب درجات الطلاب وفق القاعدة الجديدة.", _dialogs.WaitForElement("[data-testid=package-grading-note]", Wait).TextContent);
    }

    [Fact]
    public async Task Saving_without_a_change_does_not_call_the_put()
    {
        var (module, _) = PackageModule(CompletionRules.Score, PackageGradingMethods.Last);
        await StartAsync();
        OpenDialog(ModuleKinds.Scorm, module);
        _dialogs.WaitForElement("[data-testid=package-grading]", Wait);

        _dialogs.Find("[data-testid=module-save]").Click();

        _dialogs.WaitForAssertion(() => Assert.Contains(_modules.Calls, c => c.StartsWith("update")), Wait);
        Assert.Empty(_packages.GradingUpdates);
    }

    [Fact]
    public async Task A_module_without_a_score_rule_has_no_grading_choice()
    {
        var (module, _) = PackageModule(CompletionRules.Submit, PackageGradingMethods.Highest);
        await StartAsync();

        OpenDialog(ModuleKinds.Scorm, module);

        _dialogs.WaitForElement("[data-testid=module-save]", Wait);
        Assert.Empty(_dialogs.FindAll("[data-testid=package-grading]"));
    }

    [Fact]
    public async Task Other_module_kinds_never_show_it_or_read_a_package()
    {
        var page = _modules.Seed(Cc, Lesson, new CreateModuleRequest(ModuleKinds.Page, "صفحة", "Page", ModuleVisibilities.Published, null, null,
            ModuleTargets.ClassCourse, null, CompletionRules.None, null, new ModuleContentInput(BodyHtmlEn: "<p>x</p>")));
        await StartAsync();

        OpenDialog(ModuleKinds.Page, page);

        _dialogs.WaitForElement("[data-testid=module-save]", Wait);
        Assert.Empty(_dialogs.FindAll("[data-testid=package-grading]"));
        Assert.Equal(0, _packages.StatusReads);
    }

    [Fact]
    public async Task A_package_that_is_not_attached_shows_its_own_message()
    {
        var (module, _) = PackageModule(CompletionRules.Score, PackageGradingMethods.Highest);
        _packages.GradingFailure = new ApiProblemException(409, ProblemCodes.package_not_attached, null, null);
        await StartAsync();
        OpenDialog(ModuleKinds.Scorm, module);
        _dialogs.WaitForElement("[data-testid=package-grading]", Wait);
        _dialogs.Find($"[data-testid=package-grading-{PackageGradingMethods.Last}] input").Change(true);

        _dialogs.Find("[data-testid=module-save]").Click();

        var error = _dialogs.WaitForElement("[data-testid=module-error]", Wait);
        Assert.Contains("not attached", error.TextContent);
    }

    [Fact]
    public async Task The_fake_rejects_an_unknown_method_on_the_put()
    {
        var packageId = Guid.NewGuid();
        _packages.SeedAttached(packageId, Cc, Lesson, PackageKinds.Scorm, PackageGradingMethods.Highest);

        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => _packages.UpdateGradingMethodAsync(packageId, new UpdatePackageGradingMethodRequest("Average")));

        Assert.Equal(ProblemCodes.validation_failed, ex.Code);
    }
}
