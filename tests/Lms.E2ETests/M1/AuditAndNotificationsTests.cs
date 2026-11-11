using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Lms.E2ETests.M1.M1Kit;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M1;

/// <summary>M1 exit demo 3 (plan Task 38): audit viewer, notifications and blocked sign-in messages.</summary>
public sealed class AuditAndNotificationsTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    /// <summary>
    /// FR-SEC-07: with the right password, a blocked account sees why it cannot sign in (exact copy from the account
    /// resources), stays on the login page and gets no session. Seed users: deactivated-e2e, expired-e2e, withdrawn-e2e.
    /// </summary>
    [Theory]
    [InlineData("ar", "deactivated-e2e", "AccountDeactivated")]
    [InlineData("en", "deactivated-e2e", "AccountDeactivated")]
    [InlineData("ar", "expired-e2e", "AccountExpired")]
    [InlineData("en", "expired-e2e", "AccountExpired")]
    [InlineData("ar", "withdrawn-e2e", "AccountWithdrawn")]
    [InlineData("en", "withdrawn-e2e", "AccountWithdrawn")]
    public async Task Deactivated_user_sees_blocked_message_on_login(string culture, string user, string messageKey)
    {
        await StartAsync(test: $"{nameof(Deactivated_user_sees_blocked_message_on_login)}-{culture}-{user}");
        var login = new LoginPage(Page);
        await Page.GotoAsync($"/Account/Login?culture={culture}&ui-culture={culture}");
        await login.Email.WaitForAsync();
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await Page.Locator("html").GetAttributeAsync("dir"));

        await login.SubmitAsync(E2ESettings.SeedUser(user), Settings.Password);

        await Expect(login.Error).ToHaveTextAsync(Resx.Account(culture, messageKey));
        Assert.Contains("/Account/Login", Page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(await Page.Context.CookiesAsync(), c => c.Name.StartsWith(".AspNetCore.Identity", StringComparison.Ordinal));
    }

    /// <summary>
    /// FR-NOT-02: while the assignee sits signed in on the home page (browser 1), the Org Admin assigns them a role in a
    /// second browser; the bell badge and the notification text appear in the first without any reload. The assignee is the
    /// dedicated seed user notify-e2e (a Teacher in KG); the new role is Supervisor at a fresh unit, so each run is distinct.
    /// </summary>
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Assignee_sees_realtime_bell_notification_without_reload(string culture)
    {
        await StartAsync(test: $"{nameof(Assignee_sees_realtime_bell_notification_without_reload)}-{culture}");
        await SignInAsync(Page, E2ESettings.SeedUser("notify-e2e"), Settings.Password, culture);
        // Start from a known state: everything read (earlier runs leave unread notifications), so the badge is absent.
        await Page.GotoAsync("/notifications");
        await Page.GetByTestId("notif-mark-all").ClickAsync();
        var badge = Page.GetByTestId("notif-badge").Locator(".mud-badge");
        await Expect(badge).ToHaveCountAsync(0);
        await Page.GotoAsync("/");
        await new ShellPage(Page).WaitReadyAsync();
        await Page.GetByTestId("notif-bell").WaitForAsync();
        await Expect(badge).ToHaveCountAsync(0);
        await Page.EvaluateAsync("() => { window.__lmsNoReload = true; }"); // a reload would clear this marker

        // The admin works in a second, independent browser context.
        var adminContext = await Fixture.Browser.NewContextAsync(new Microsoft.Playwright.BrowserNewContextOptions
        {
            BaseURL = Settings.BaseUrl.ToString(),
            ViewportSize = new Microsoft.Playwright.ViewportSize { Width = 1280, Height = 800 },
        });
        adminContext.SetDefaultTimeout(DefaultTimeoutMs);
        var admin = await adminContext.NewPageAsync();
        var run = RunId();
        string unitAr = $"فصل إشعار {run}", unitEn = $"Notify section {run}";
        try
        {
            await SignInAsync(admin, E2ESettings.SeedUser("orgadmin"), Settings.Password, culture);
            var tree = new OrgTreePage(admin);
            await tree.GotoAsync();
            await tree.AddChildAsync(SchoolName(culture), $"مرحلة إشعار {run}", $"Notify stage {run}", $"NS{run}", StageLevel(culture));
            await tree.AddChildAsync(Pick(culture, $"مرحلة إشعار {run}", $"Notify stage {run}"), unitAr, unitEn, $"NU{run}", SectionLevel(culture));

            await admin.GotoAsync("/assignments");
            await admin.GetByTestId("assign-new").WaitForAsync();
            await admin.GetByTestId("assign-new").ClickAsync();
            // The person comes from the staff picker (people with a staff profile); notify-e2e has one in the dev seed.
            await MudControls.AutocompleteAsync(admin, admin.Locator("input[data-testid='assign-person']"),
                E2ESettings.SeedUser("notify-e2e"), E2ESettings.SeedUser("notify-e2e"));
            await MudControls.SelectAsync(admin, admin.GetByTestId("assign-role"), SupervisorRole(culture));
            await MudControls.SelectAsync(admin, admin.GetByTestId("assign-unit"), Pick(culture, unitAr, unitEn));
            await admin.GetByTestId("assign-save").ClickAsync();
            await Expect(admin.Locator(".mud-dialog")).ToHaveCountAsync(0);

            // Browser 1: the badge grows and the new entry reads as the assignment, with no reload in between.
            await Expect(badge).ToHaveTextAsync("1");
            await Page.GetByTestId("notif-toggle").ClickAsync();
            var role = SupervisorRole(culture);
            await Expect(Page.Locator("[data-testid^='notif-item-']").First).ToContainTextAsync(role);
            await Expect(Page.Locator("[data-testid^='notif-item-']").First).ToContainTextAsync(Pick(culture, unitAr, unitEn));
            Assert.True(await Page.EvaluateAsync<bool>("() => window.__lmsNoReload === true"), "the page was reloaded");
        }
        finally
        {
            // Clean up even when an assertion failed: take the new role away again, so notify-e2e has a single context at
            // the start of every run (with two, the app opens the context chooser, which has no bell).
            await RemoveAddedRoleAsync(admin, culture, unitAr, unitEn);
            await adminContext.DisposeAsync();
        }
    }

    /// <summary>
    /// Removes the Supervisor assignment this run added (if it got that far), looking only at rows of the notify-e2e user
    /// with that role in the run's own unit, so no other assignment can be touched.
    /// </summary>
    private static async Task RemoveAddedRoleAsync(Microsoft.Playwright.IPage admin, string culture, string unitAr, string unitEn)
    {
        try
        {
            await admin.GotoAsync("/assignments");
            await admin.GetByTestId("unit-picker").WaitForAsync();
            var option = Pick(culture, unitAr, unitEn);
            await admin.GetByTestId("unit-picker").ClickAsync();
            var item = admin.Locator(".mud-popover-open .mud-list-item").Filter(new() { HasText = option });
            if (await item.CountAsync() == 0)
            {
                return; // the unit was never created: nothing to clean up
            }

            await item.First.ClickAsync();
            var mine = admin.Locator("tr")
                .Filter(new() { HasText = E2ESettings.SeedUser("notify-e2e") })
                .Filter(new() { HasText = SupervisorRole(culture) })
                .Filter(new() { HasText = option });
            await Expect(admin.Locator("[data-testid='unit-picker']")).ToContainTextAsync(option);
            while (await mine.CountAsync() > 0)
            {
                var before = await mine.CountAsync();
                await mine.First.Locator("[data-testid^='assignment-delete-']").ClickAsync();
                await admin.GetByTestId("confirm-yes").ClickAsync();
                await Expect(mine).ToHaveCountAsync(before - 1);
            }
        }
        catch (Exception ex) when (ex is Microsoft.Playwright.PlaywrightException or TimeoutException)
        {
            // Best effort: when the run failed early there is nothing to remove, and this must not hide the real failure.
        }
    }

    /// <summary>
    /// FR-AUD-03/04: moving a section from one stage to another shows up as the newest "unit updated" entry with the old and
    /// new parent side by side, and the filtered list exports as CSV (UTF-8 with BOM, so Excel reads Arabic) and as XLSX.
    /// </summary>
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Audit_viewer_shows_unit_move_with_before_after_and_exports_csv_and_xlsx(string culture)
    {
        await StartAsync(test: $"{nameof(Audit_viewer_shows_unit_move_with_before_after_and_exports_csv_and_xlsx)}-{culture}");
        await SignInAsync(Page, E2ESettings.SeedUser("orgadmin"), Settings.Password, culture);
        var run = RunId();
        string fromAr = $"مرحلة أ {run}", fromEn = $"Stage A {run}", toAr = $"مرحلة ب {run}", toEn = $"Stage B {run}";
        string sectionAr = $"فصل منقول {run}", sectionEn = $"Moved section {run}";

        var tree = new OrgTreePage(Page);
        await tree.GotoAsync();
        var fromId = await tree.AddChildAsync(SchoolName(culture), fromAr, fromEn, $"FA{run}", StageLevel(culture));
        var toId = await tree.AddChildAsync(SchoolName(culture), toAr, toEn, $"TB{run}", StageLevel(culture));
        await tree.AddChildAsync(Pick(culture, fromAr, fromEn), sectionAr, sectionEn, $"MS{run}", SectionLevel(culture));
        await tree.MoveAsync(Pick(culture, sectionAr, sectionEn), Pick(culture, toAr, toEn));

        var audit = new AuditPage(Page);
        await audit.GotoAsync();
        await audit.FilterAsync(Text(culture, "AuditEntity_OrgUnit"), Text(culture, "AuditAction_Updated"));
        await audit.OpenNewestAsync();

        var parent = audit.Diff("ParentId");
        await Expect(parent.Locator("td").Nth(1)).ToHaveTextAsync(fromId.ToString());
        await Expect(parent.Locator("td").Nth(2)).ToHaveTextAsync(toId.ToString());
        await Expect(audit.Diff("Path").Locator("td").Nth(1)).ToContainTextAsync(fromId.ToString("N")); // paths hold N-format ids
        await Expect(audit.Diff("Path").Locator("td").Nth(2)).ToContainTextAsync(toId.ToString("N"));
        await Page.GetByTestId("audit-drawer-close").ClickAsync();

        var (csv, csvName) = await audit.ExportAsync("audit-export-csv");
        Assert.EndsWith(".csv", csvName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, csv.Take(3).ToArray());
        Assert.True(System.Text.Encoding.UTF8.GetString(csv).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length > 1, "header only");

        var (xlsx, xlsxName) = await audit.ExportAsync("audit-export-xlsx");
        Assert.EndsWith(".xlsx", xlsxName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("PK"u8.ToArray(), xlsx.Take(2).ToArray()); // an Office Open XML package is a zip
    }
}
