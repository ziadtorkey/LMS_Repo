using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests;

/// <summary>Login page behaviour seen by a user, and the shell at phone width.</summary>
public sealed class LoginTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Locked_out_after_five_failed_logins_with_same_message_as_unknown_user()
    {
        // Dedicated seed user: locking a shared user would break the other tests for 15 minutes.
        await StartAsync();
        var login = new LoginPage(Page);
        await Page.GotoAsync("/");
        await login.Email.WaitForAsync();

        await login.SubmitAsync($"nobody-{Guid.NewGuid():N}@{E2ESettings.SeedDomain}", "Wrong-Passw0rd");
        var unknownMessage = await login.Error.InnerTextAsync();

        var lockoutUser = E2ESettings.SeedUser("lockout-e2e");
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await login.SubmitAsync(lockoutUser, "Wrong-Passw0rd");
        }

        await login.SubmitAsync(lockoutUser, Settings.Password);

        await Expect(login.Error).ToBeVisibleAsync();
        Assert.Equal(unknownMessage, await login.Error.InnerTextAsync());
        Assert.Contains("/Account/Login", Page.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_page_is_arabic_rtl_by_default()
    {
        await StartAsync();
        await Page.GotoAsync("/");
        await new LoginPage(Page).Email.WaitForAsync();

        Assert.Equal("rtl", await Page.Locator("html").GetAttributeAsync("dir"));
        Assert.Equal("ar", await Page.Locator("html").GetAttributeAsync("lang"));
    }

    [Fact]
    public async Task Shell_is_usable_at_360px_in_arabic()
    {
        await StartAsync(width: 360, height: 740);
        var shell = new ShellPage(Page);

        await new LoginPage(Page).SignInAsync(Settings.MultiContextEmail, Settings.Password);
        await new ChooseContextPage(Page).WaitAsync();
        await new ChooseContextPage(Page).ChooseAsync("المشرف الأكاديمي");
        await shell.Root.WaitForAsync();

        var noHorizontalScroll = await Page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth <= document.documentElement.clientWidth");
        Assert.True(noHorizontalScroll, "page scrolls horizontally at 360px");

        await shell.NavToggle.ClickAsync();
        await Expect(shell.Nav("home")).ToBeVisibleAsync();
        await Expect(shell.Nav("orgunits")).ToBeVisibleAsync();
    }
}
