using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.M1;
using Lms.E2ETests.M2;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M3;

/// <summary>
/// M3 exit demo 2 (plan Task 47, #255), guardian part: the guardian portal switched on for the school group, an invited
/// guardian activates from the email, signs in and sees only their own children under "My children".
/// </summary>
public sealed class GuardianPortalTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    private const string Flag = "Features.GuardianPortal";

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Invited_guardian_activates_signs_in_and_sees_only_their_own_children(string culture)
    {
        var names = M2Names.New();
        await StartAsync(culture: culture);
        await M1Kit.SignInAsync(Page, E2ESettings.SeedUser("orgadmin"), Settings.Password, culture);
        var api = new E2EApi(Page, Settings.BaseUrl);
        var kg = await M3Kit.EnsureKgSectionAsync(api);
        var school = await SchoolUnitAsync(api);

        // The portal is off by default (D30): switch it on for the school group, then enrol two students of different families.
        var set = await api.SendAsync("PUT", $"/api/org-units/{school}/settings/{Flag}", new { valueJson = "true", isLocked = false });
        Assert.True(set.Ok, $"switching the portal on answered {set.Status}: {await set.TextAsync()}");
        try
        {
            var mine = await M3Kit.CreateStudentAsync(api, kg, names, "Mine", slot: 1);
            var other = await M3Kit.CreateStudentAsync(api, kg, names, "Other", slot: 2);

            // The invitation email carries the activation link; the guardian sets a password and signs in.
            await Page.Context.ClearCookiesAsync();
            await Page.EvaluateAsync("() => { sessionStorage.clear(); localStorage.removeItem('lms.context'); }");
            using var mail = new MailpitClient();
            var link = await mail.WaitForAccountLinkAsync(mine.GuardianEmail);
            await new ResetPasswordPage(Page).SetPasswordAsync(link, names.NewPassword);
            await M1Kit.SignInAsync(Page, mine.GuardianEmail, names.NewPassword, culture);
            var shell = new ShellPage(Page);
            await Expect(shell.ContextSwitcher).ToBeVisibleAsync();
            await Page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");

            // "My children" lists the guardian's own child with the student code, and not the other family's.
            await Page.GotoAsync("/me/children");
            await Page.GetByTestId("children-title").WaitForAsync();
            var card = Page.GetByTestId($"child-card-{mine.Id}");
            await card.WaitForAsync();
            await Expect(card).ToContainTextAsync(culture == "ar" ? mine.NameAr : mine.NameEn);
            await Expect(Page.Locator("[data-testid^='child-card-']")).ToHaveCountAsync(1);
            await Expect(Page.GetByTestId($"child-card-{other.Id}")).ToHaveCountAsync(0);

            // The API agrees: a guardian holds no student-read right at all, so any student (the other family's too) is refused.
            var guardianApi = new E2EApi(Page, Settings.BaseUrl);
            Assert.Equal(403, (await guardianApi.SendAsync("GET", $"/api/students/{other.Id}")).Status);
            Assert.Equal(403, (await guardianApi.SendAsync("GET", $"/api/students/{mine.Id}")).Status);
        }
        finally
        {
            await CleanUpFlagAsync(culture, school);
        }
    }

    private static async Task<Guid> SchoolUnitAsync(E2EApi api)
    {
        var units = await api.JsonAsync(await api.SendAsync("GET", "/api/org-units"));
        return units.EnumerateArray().Single(u => u.GetProperty("code").GetString() == "S").GetProperty("id").GetGuid();
    }

    /// <summary>Puts the shared school group back to the default so the other scenarios are not invited to the portal.</summary>
    private async Task CleanUpFlagAsync(string culture, Guid school)
    {
        await Page.Context.ClearCookiesAsync();
        await Page.EvaluateAsync("() => { sessionStorage.clear(); localStorage.removeItem('lms.context'); }");
        await M1Kit.SignInAsync(Page, E2ESettings.SeedUser("orgadmin"), Settings.Password, culture);
        var api = new E2EApi(Page, Settings.BaseUrl);
        await Expect(new ShellPage(Page).ContextSwitcher).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        await api.SendAsync("DELETE", $"/api/org-units/{school}/settings/{Flag}");
    }
}
