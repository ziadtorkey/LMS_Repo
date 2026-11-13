using System.Globalization;
using Bunit;
using Lms.Client.Components.Editors;
using Lms.Client.Pages.Content;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Content;

public sealed class ModuleViewerPageTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = Guid.NewGuid(), Lesson = Guid.NewGuid();

    private readonly FakeModulesApi _modules = new() { AsStudent = true, CanEdit = false };
    private readonly FakeAssetsApi _assets = new();

    public ModuleViewerPageTests()
    {
        Services.AddSingleton<IModulesApi>(_modules);
        Services.AddSingleton<IAssetsApi>(_assets);
        Culture.Current = new CultureInfo("en");
    }

    private ModuleDetailDto Seed(string title, string kind = ModuleKinds.Page, string rule = CompletionRules.None,
        ModuleContentInput? content = null, AssetDto? asset = null) =>
        _modules.Seed(Cc, Lesson, new CreateModuleRequest(kind, title + " ar", title, ModuleVisibilities.Published, null, null,
            ModuleTargets.ClassCourse, null, rule, null, content ?? new ModuleContentInput()), asset);

    private async Task<IRenderedComponent<ModuleViewerPage>> OpenAsync(ModuleDetailDto module, bool providers = true)
    {
        if (providers)
        {
            await GrantAsync(Permissions.Modules.View, Permissions.Files.Download);
            Render<MudPopoverProvider>();
        }
        var page = Render<ModuleViewerPage>(p => p.Add(x => x.Id, Cc).Add(x => x.ModuleId, module.Node.Id));
        page.WaitForElement("[data-testid=module-title], [data-testid=module-load-error]", Wait);
        return page;
    }

    [Fact]
    public async Task Page_renders_server_html_via_SanitizedHtml_only()
    {
        var page = await OpenAsync(Seed("Intro", content: new ModuleContentInput(BodyHtmlEn: "<p>Hello <strong>world</strong></p>", BodyHtmlAr: "<p>مرحبا</p>")));

        var html = page.FindComponent<SanitizedHtml>();
        Assert.Equal("<p>Hello <strong>world</strong></p>", html.Instance.Html);
        Assert.Contains("<strong>world</strong>", page.Find("[data-testid=module-page-html]").InnerHtml);
        Assert.Single(page.FindComponents<SanitizedHtml>());
    }

    [Fact]
    public async Task Page_falls_back_to_the_other_language()
    {
        var page = await OpenAsync(Seed("Intro", content: new ModuleContentInput(BodyHtmlAr: "<p>عربي فقط</p>")));

        Assert.Contains("عربي فقط", page.Find("[data-testid=module-page-html]").TextContent);
    }

    [Fact]
    public async Task Sanitized_html_adds_rel_to_links_without_one()
    {
        var cut = Render<SanitizedHtml>(p => p.Add(x => x.Html, "<p><a href=\"https://a.test\">x</a> <a href=\"https://b.test\" rel=\"nofollow\">y</a></p>"));

        Assert.Contains("<a rel=\"noopener noreferrer\" href=\"https://a.test\"", cut.Markup);
        Assert.Contains("rel=\"nofollow\"", cut.Markup);
    }

    [Fact]
    public async Task Video_uses_signed_url()
    {
        var asset = new AssetDto(Guid.NewGuid(), Cc, AssetKinds.Video, "lesson.mp4", "video/mp4", 10, 120, true, DateTime.UtcNow);
        var page = await OpenAsync(Seed("Watch", ModuleKinds.Video, asset: asset));

        page.WaitForAssertion(() => Assert.StartsWith("https://files.test/", page.Find("[data-testid=asset-video]").GetAttribute("src")));
        Assert.Equal(asset.Id, Assert.Single(_assets.UrlRequests));
    }

    [Fact]
    public async Task Link_opens_in_a_new_tab_with_noopener()
    {
        var page = await OpenAsync(Seed("Site", ModuleKinds.Link, content: new ModuleContentInput(Url: "https://example.test/x")));

        var link = page.Find("[data-testid=module-link]");
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
    }

    [Fact]
    public async Task Link_with_a_blocked_scheme_is_not_rendered_as_a_link()
    {
        var page = await OpenAsync(Seed("Bad", ModuleKinds.Link, content: new ModuleContentInput(Url: "javascript:alert(1)")));

        Assert.Empty(page.FindAll("[data-testid=module-link]"));
        Assert.NotNull(page.Find("[data-testid=module-link-blocked]"));
    }

    [Fact]
    public async Task Manual_completion_marks_and_updates_state()
    {
        var module = Seed("Do it", rule: CompletionRules.Manual, content: new ModuleContentInput(BodyHtmlEn: "<p>x</p>"));
        var page = await OpenAsync(module);

        page.Find("[data-testid=module-mark-complete]").Click();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid=module-done]")));
        Assert.Empty(page.FindAll("[data-testid=module-mark-complete]"));
        Assert.Equal(CompletionStates.Completed, (await _modules.GetCompletionAsync(module.Node.Id)).State);
    }

    [Fact]
    public async Task Mark_complete_is_only_offered_for_the_manual_rule()
    {
        var page = await OpenAsync(Seed("Plain", rule: CompletionRules.View, content: new ModuleContentInput(BodyHtmlEn: "<p>x</p>")));

        Assert.Empty(page.FindAll("[data-testid=module-mark-complete]"));
    }

    [Fact]
    public async Task Previous_next_navigation()
    {
        var a = Seed("A", content: new ModuleContentInput(BodyHtmlEn: "<p>a</p>"));
        var b = Seed("B", content: new ModuleContentInput(BodyHtmlEn: "<p>b</p>"));
        var c = Seed("C", content: new ModuleContentInput(BodyHtmlEn: "<p>c</p>"));

        var page = await OpenAsync(b);

        Assert.EndsWith($"class-courses/{Cc}/modules/{a.Node.Id}", page.Find("[data-testid=module-prev]").GetAttribute("href"));
        Assert.EndsWith($"class-courses/{Cc}/modules/{c.Node.Id}", page.Find("[data-testid=module-next]").GetAttribute("href"));

        var first = await OpenAsync(a, providers: false);
        Assert.Empty(first.FindAll("[data-testid=module-prev]"));
    }

    [Fact]
    public async Task Without_Modules_View_the_page_is_forbidden()
    {
        await GrantAsync(Permissions.LessonPlans.View);
        var page = Render<ModuleViewerPage>(p => p.Add(x => x.Id, Cc).Add(x => x.ModuleId, Guid.NewGuid()));

        page.WaitForElement("[data-testid=module-forbidden]", Wait);
    }
}
