using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.M1;
using Lms.E2ETests.M2;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M3;

/// <summary>
/// M3 exit demo 2 (plan Task 47, #255), students part: creation with a class, one-time credentials, duplicate prompt.
/// Runs as the seeded school group's Org Admin with unique people per test (<see cref="M2Names"/>); classes are defined
/// through the API in the current year at the KG stage.
/// </summary>
public sealed class StudentsTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Same_name_and_dob_asks_for_confirmation(string culture)
    {
        var names = M2Names.New();
        await StartAsync(culture: culture);
        await M1Kit.SignInAsync(Page, E2ESettings.SeedUser("orgadmin"), Settings.Password, culture);
        var form = new StudentFormPage(Page);

        // The first student is created without any warning.
        await form.OpenAsync();
        await form.FillAsync(Student(names, culture, nationalId: names.NationalId(1)));
        await form.SaveAsync();
        await Expect(form.CredentialsDialog).ToBeVisibleAsync();
        await form.CloseCredentialsAsync();

        // The same name and birth date (another national id) asks for confirmation; cancelling creates nobody.
        await form.OpenAsync();
        await form.FillAsync(Student(names, culture, nationalId: names.NationalId(2)));
        await form.SaveAsync();
        var prompt = Page.Locator(".mud-message-box");
        await Expect(prompt).ToBeVisibleAsync();
        await prompt.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = Resx.In(culture, "Common_Cancel") }).ClickAsync();
        await Expect(prompt).ToHaveCountAsync(0);
        Assert.Equal(1, await CountStudentsAsync(names));

        // Confirming creates the second student.
        await form.SaveAsync();
        await Expect(prompt).ToBeVisibleAsync();
        await prompt.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = Resx.In(culture, "Student_CreateAnyway") }).ClickAsync();
        await Expect(form.CredentialsDialog).ToBeVisibleAsync();
        await form.CloseCredentialsAsync();
        Assert.Equal(2, await CountStudentsAsync(names));
        await AssertDirectionAsync(culture);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Admin_creates_student_in_a_class_with_a_new_guardian_then_sibling_links_the_same_guardian(string culture)
    {
        var names = M2Names.New();
        await StartAsync(culture: culture);
        await M1Kit.SignInAsync(Page, E2ESettings.SeedUser("orgadmin"), Settings.Password, culture);
        var api = new E2EApi(Page, Settings.BaseUrl);
        var kg = await M3Kit.EnsureKgSectionAsync(api);
        var cls = await M3Kit.CreateClassAsync(api, kg, names, capacity: 2);
        var className = culture == "ar" ? cls.NameAr : cls.NameEn;
        var guardian = new StudentFormPage.NewGuardian(names.NameAr("ولي"), names.NameEn("Guardian"), names.NationalId(9), names.Mobile, names.Email("guardian"));
        var form = new StudentFormPage(Page);

        // The first child: auto code, class (its fill is shown), a new primary guardian.
        await form.OpenAsync();
        await Expect(Page.GetByTestId("student-code-auto")).ToBeVisibleAsync();
        await form.FillAsync(Student(names, culture, names.NationalId(1), className: $"{className} (0/2)"));
        await form.FillNewGuardianAsync(0, guardian);
        await form.SaveAsync();
        await Expect(form.CredentialsDialog).ToBeVisibleAsync();
        var code = await form.Field("credentials-code").InputValueAsync();
        await form.CloseCredentialsAsync();

        var first = await FindStudentAsync(api, names.NameEn("Student"));
        await form.OpenProfileAsync(first.GetProperty("summary").GetProperty("id").GetGuid());
        await Expect(Page.GetByTestId("profile-code")).ToHaveTextAsync(code);
        await Expect(Page.GetByTestId("profile-class")).ToContainTextAsync(className);
        await Page.GetByRole(Microsoft.Playwright.AriaRole.Tab, new() { Name = Resx.In(culture, "Student_Guardians") }).ClickAsync();
        await Expect(Page.GetByTestId("guardians-table")).ToContainTextAsync(culture == "ar" ? guardian.NameAr : guardian.NameEn);

        // The sibling: typing the same national id offers the registered guardian; linking it creates no second one.
        await form.OpenAsync();
        await form.FillAsync(new StudentFormPage.NewStudent(
            names.NameAr("أخ"), names.NameEn("Sibling"), new DateOnly(DateTime.Today.Year - 6, 5, 20), Resx.In(culture, "Gender_Female"),
            Resx.In(culture, "Religion_Muslim"), culture == "ar" ? "مصر" : "Egypt", names.NationalId(2),
            culture == "ar" ? "الفصل 1" : "Section 1", culture == "ar" ? "رياض الأطفال" : "KG", ClassText: $"{className} (1/2)"));
        await form.Field("guardian-national-id-0").FillAsync(guardian.NationalId);
        await Expect(Page.GetByTestId("guardian-match-0")).ToBeVisibleAsync();
        await Page.GetByTestId("guardian-link-0").ClickAsync();
        await Expect(Page.GetByTestId("guardian-linked-0")).ToBeVisibleAsync();
        await form.SaveAsync();
        await Expect(form.CredentialsDialog).ToBeVisibleAsync();
        await form.CloseCredentialsAsync();

        var second = await FindStudentAsync(api, names.NameEn("Sibling"));
        Assert.Equal(
            first.GetProperty("guardians")[0].GetProperty("guardianId").GetGuid(),
            second.GetProperty("guardians")[0].GetProperty("guardianId").GetGuid());
        Assert.Equal(1, second.GetProperty("guardians")[0].GetProperty("otherChildrenInScope").GetInt32()); // the first child
        await AssertDirectionAsync(culture);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Credentials_dialog_shows_password_once_and_delivery(string culture)
    {
        var names = M2Names.New();
        await StartAsync(culture: culture);
        await M1Kit.SignInAsync(Page, E2ESettings.SeedUser("orgadmin"), Settings.Password, culture);
        var api = new E2EApi(Page, Settings.BaseUrl);
        var form = new StudentFormPage(Page);

        // A student with an e-mail gets the credentials there; the dialog shows them once.
        await form.OpenAsync();
        await form.FillAsync(Student(names, culture, names.NationalId(1), email: names.Email("student")));
        await form.SaveAsync();
        await Expect(form.CredentialsDialog).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("credentials-once")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("credentials-delivery")).ToHaveTextAsync(Resx.In(culture, "CredentialsDelivery_Student"));
        var password = await form.Field("credentials-password").InputValueAsync();
        Assert.True(password.Length >= 8, "a generated password is shown");
        await form.CloseCredentialsAsync();

        // Afterwards it is gone: not on the profile page, not in any API answer.
        var student = await FindStudentAsync(api, names.NameEn("Student"));
        var id = student.GetProperty("summary").GetProperty("id").GetGuid();
        await form.OpenProfileAsync(id);
        Assert.DoesNotContain(password, await Page.ContentAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(password, await (await api.SendAsync("GET", $"/api/students/{id}")).TextAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(password, await (await api.SendAsync("GET", $"/api/students?search={names.Suffix}")).TextAsync(), StringComparison.Ordinal);
    }

    /// <summary>D24 / D47: the student signs in with <c>code@shortcode</c> and the default password and must set their own first.</summary>
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Student_signs_in_with_default_password_and_must_change_it_first(string culture)
    {
        var names = M2Names.New();
        var email = names.Email("guardian");
        await StartAsync(culture: culture);
        await M1Kit.SignInAsync(Page, E2ESettings.SeedUser("orgadmin"), Settings.Password, culture);
        var form = new StudentFormPage(Page);
        await form.OpenAsync();
        // No student e-mail: the account name is code@shortcode and the credentials go to the primary guardian.
        await form.FillAsync(Student(names, culture, names.NationalId(1)));
        await form.FillNewGuardianAsync(0, new StudentFormPage.NewGuardian(names.NameAr("ولي"), names.NameEn("Guardian"), names.NationalId(9), names.Mobile, email));
        await form.SaveAsync();
        await Expect(form.CredentialsDialog).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("credentials-delivery")).ToHaveTextAsync(Resx.In(culture, "CredentialsDelivery_PrimaryGuardian"));
        var userName = $"{(await form.Field("credentials-code").InputValueAsync()).ToLowerInvariant()}@altagamo3";
        var defaultPassword = await form.Field("credentials-password").InputValueAsync();
        await form.CloseCredentialsAsync();

        // The credentials were also e-mailed to the primary guardian.
        using (var mail = new MailpitClient())
        {
            await WaitUntilAsync(async () => await mail.CountAsync(email) > 0);
        }

        // First sign-in: straight to the change-password page, and the app stays out of reach until it is done.
        await SignOutAsync();
        var login = new LoginPage(Page);
        await login.SignInAsync(userName, defaultPassword);
        await Page.WaitForURLAsync(ChangePasswordPage.Url);
        await Expect(Page.GetByTestId("change-intro")).ToBeVisibleAsync();
        await Page.GotoAsync("/");
        await Page.WaitForURLAsync(ChangePasswordPage.Url);

        var change = new ChangePasswordPage(Page);
        // Wrong current password, mismatching confirmation and a weak new password are all refused.
        var newPassword = names.NewPassword;
        await change.SubmitAsync("Wrong-Current-1", newPassword, newPassword);
        await Expect(change.Error).ToHaveTextAsync(Resx.Account(culture, "ChangePassword_Failed"));
        await change.SubmitAsync(defaultPassword, newPassword, newPassword + "x");
        await Expect(change.Error).ToHaveTextAsync(Resx.Account(culture, "ChangePassword_Mismatch"));
        await change.SubmitAsync(defaultPassword, "weak", "weak");
        await Expect(change.Error).ToHaveTextAsync(Resx.Account(culture, "ChangePassword_Failed"));

        // A valid change lands in the app.
        await change.SubmitAsync(defaultPassword, newPassword, newPassword);
        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Account/", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

        // The next sign-in with the new password skips the page; the default password no longer works.
        await SignOutAsync();
        await login.SignInAsync(userName, newPassword);
        await shell.WaitReadyAsync();
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Account/", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        await AssertDirectionAsync(culture);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition was not met within 30 s.");
            await Task.Delay(500);
        }
    }

    /// <summary>Signs out of the app and the identity server so the next sign-in starts clean.</summary>
    private async Task SignOutAsync()
    {
        await Page.Context.ClearCookiesAsync();
        await Page.EvaluateAsync("() => { sessionStorage.clear(); localStorage.removeItem('lms.context'); }");
    }

    private static async Task<System.Text.Json.JsonElement> FindStudentAsync(E2EApi api, string nameEn)
    {
        var list = await api.JsonAsync(await api.SendAsync("GET", $"/api/students?search={Uri.EscapeDataString(nameEn)}"));
        var id = list.GetProperty("items").EnumerateArray().Single(s => s.GetProperty("nameEn").GetString() == nameEn).GetProperty("id").GetGuid();
        return await api.JsonAsync(await api.SendAsync("GET", $"/api/students/{id}"));
    }

    private async Task<int> CountStudentsAsync(M2Names names)
    {
        var students = await new E2EApi(Page, Settings.BaseUrl).JsonAsync(
            await new E2EApi(Page, Settings.BaseUrl).SendAsync("GET", $"/api/students?search={Uri.EscapeDataString(names.Suffix)}"));
        return students.GetProperty("items").GetArrayLength();
    }

    private static StudentFormPage.NewStudent Student(M2Names names, string culture, string nationalId, string? className = null, string? email = null) =>
        new(names.NameAr("طالب"), names.NameEn("Student"), new DateOnly(DateTime.Today.Year - 8, 3, 15),
            Resx.In(culture, "Gender_Male"), Resx.In(culture, "Religion_Muslim"), culture == "ar" ? "مصر" : "Egypt", nationalId,
            HomeUnitText: culture == "ar" ? "الفصل 1" : "Section 1", HomeUnitAfter: culture == "ar" ? "رياض الأطفال" : "KG", ClassText: className, Email: email);

    private async Task AssertDirectionAsync(string culture) =>
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
}
