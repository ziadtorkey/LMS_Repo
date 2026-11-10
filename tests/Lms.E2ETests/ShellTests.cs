using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests;

/// <summary>M0 smoke tests (plan Task 12): real OIDC login against the seeded dev data, Arabic RTL by default.</summary>
public sealed class ShellTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    // Built-in role labels for a school-group client (BuiltInRoles, Lms.Shared).
    private const string SupervisorLabelAr = "المشرف الأكاديمي";
    private const string TeacherLabelAr = "المعلم";

    [Fact]
    public async Task Platform_admin_logs_in_and_sees_clients_menu()
    {
        await StartAsync();
        var shell = new ShellPage(Page);

        await new LoginPage(Page).SignInAsync(Settings.PlatformAdminEmail, Settings.Password);
        await shell.WaitReadyAsync();

        await Expect(shell.Nav("clients")).ToBeVisibleAsync();
        await Expect(shell.Nav("clients")).ToContainTextAsync(Resx.Ar("Nav_Clients"));
        Assert.DoesNotContain("/choose-context", Page.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Multi_context_user_chooses_context_then_sees_arabic_rtl_shell()
    {
        await StartAsync();
        var shell = new ShellPage(Page);
        var chooser = new ChooseContextPage(Page);

        await new LoginPage(Page).SignInAsync(Settings.MultiContextEmail, Settings.Password);
        await chooser.WaitAsync();
        await Expect(chooser.Options).ToHaveCountAsync(2);

        await chooser.ChooseAsync(SupervisorLabelAr);
        await shell.WaitReadyAsync();

        Assert.Equal("rtl", await shell.HtmlDirAsync());
        Assert.Equal("ar", await shell.HtmlLangAsync());
        Assert.Equal("rtl", await shell.RootDirAsync());
        await Expect(shell.ContextSwitcher).ToContainTextAsync(SupervisorLabelAr);
        await Expect(shell.Nav("home")).ToContainTextAsync(Resx.Ar("Nav_Home"));
        await Expect(shell.Nav("orgunits")).ToContainTextAsync(Resx.Ar("Nav_OrgUnits"));
        await Expect(shell.Nav("clients")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Teacher_does_not_see_roles_or_audit_menu()
    {
        await StartAsync();
        var shell = new ShellPage(Page);

        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("teacher"), Settings.Password);
        await shell.WaitReadyAsync();

        // A single assignment is selected automatically, so the chooser never appears.
        Assert.DoesNotContain("/choose-context", Page.Url, StringComparison.Ordinal);
        await Expect(shell.ContextSwitcher).ToContainTextAsync(TeacherLabelAr);
        await Expect(shell.Nav("roles")).ToHaveCountAsync(0);
        await Expect(shell.Nav("audit")).ToHaveCountAsync(0);
        await Expect(shell.Nav("clients")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Switching_language_to_english_renders_ltr()
    {
        await StartAsync();
        var shell = new ShellPage(Page);

        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("teacher"), Settings.Password);
        await shell.WaitReadyAsync();
        Assert.Equal("rtl", await shell.HtmlDirAsync());

        await shell.SwitchLanguageAsync();

        Assert.Equal("ltr", await shell.HtmlDirAsync());
        Assert.Equal("en", await shell.HtmlLangAsync());
        Assert.Equal("ltr", await shell.RootDirAsync());
        await Expect(shell.Nav("home")).ToContainTextAsync(Resx.En("Nav_Home"));

        // The choice survives a reload.
        await Page.ReloadAsync();
        await shell.WaitReadyAsync();
        Assert.Equal("ltr", await shell.HtmlDirAsync());
    }

    [Fact]
    public async Task Context_switch_updates_menu()
    {
        await StartAsync();
        var shell = new ShellPage(Page);
        var chooser = new ChooseContextPage(Page);

        await new LoginPage(Page).SignInAsync(Settings.MultiContextEmail, Settings.Password);
        await chooser.WaitAsync();
        await chooser.ChooseAsync(TeacherLabelAr);
        await shell.WaitReadyAsync();
        await Expect(shell.Nav("orgunits")).ToHaveCountAsync(0);

        await shell.ContextSwitcher.ClickAsync();
        await chooser.WaitAsync();
        await chooser.ChooseAsync(SupervisorLabelAr);
        await shell.WaitReadyAsync();

        await Expect(shell.Nav("orgunits")).ToBeVisibleAsync();
        await Expect(shell.ContextSwitcher).ToContainTextAsync(SupervisorLabelAr);
    }

    [Fact(Skip = "Bug #84: a stale stored context shows Forbidden instead of the chooser. Remove the skip when #84 is fixed.")]
    public async Task Stale_stored_context_leads_to_chooser_not_forbidden()
    {
        // QA follow-up from #28: an assignment id the user no longer has must send them to the chooser.
        await StartAsync();
        var chooser = new ChooseContextPage(Page);

        await new LoginPage(Page).SignInAsync(Settings.MultiContextEmail, Settings.Password);
        await chooser.WaitAsync();
        await Page.EvaluateAsync("() => localStorage.setItem('lms.context', '11111111-2222-3333-4444-555555555555')");

        await Page.GotoAsync("/");

        await chooser.WaitAsync();
        await Expect(new ShellPage(Page).ForbiddenPage).ToHaveCountAsync(0);
        await Expect(chooser.Options).ToHaveCountAsync(2);
    }
}
