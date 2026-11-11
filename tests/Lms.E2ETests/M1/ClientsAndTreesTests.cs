using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M1;

/// <summary>
/// M1 exit demo 1 (plan Task 36, #71): clients with first admins, the invitation email, level types and trees, in
/// Arabic (RTL) and English. Each test creates its own client with unique names (<see cref="M1Names"/>).
/// </summary>
public sealed class ClientsAndTreesTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Platform_admin_creates_school_group_and_university_with_first_admins(string culture)
    {
        var names = M1Names.New();
        await StartAsync(culture: culture);
        await SignInAsPlatformAdminAsync();
        await AssertDirectionAsync(culture);

        var clients = new ClientsAdminPage(Page, culture);
        await clients.OpenAsync();
        await clients.CreateAsync(SchoolGroup(names));
        await clients.CreateAsync(University(names));

        var schools = await clients.FindRowAsync(culture == "ar" ? names.SchoolGroupAr : names.SchoolGroupEn);
        await Expect(schools).ToContainTextAsync(Resx.In(culture, "ClientType_SchoolGroup"));
        await Expect(schools).ToContainTextAsync(Resx.In(culture, "ClientStatus_Active"));

        var university = await clients.FindRowAsync(culture == "ar" ? names.UniversityAr : names.UniversityEn);
        await Expect(university).ToContainTextAsync(Resx.In(culture, "ClientType_University"));

        // Both first admins were invited (new users).
        using var mail = new MailpitClient();
        Assert.Contains("/Account/ResetPassword", await mail.WaitForAccountLinkAsync(names.AdminEmail("school")));
        Assert.Contains("/Account/ResetPassword", await mail.WaitForAccountLinkAsync(names.AdminEmail("uni")));
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Org_admin_sets_password_from_invitation_email_and_logs_in(string culture)
    {
        var names = M1Names.New();
        await StartAsync(culture: culture);
        await SignInAsPlatformAdminAsync();
        var clients = new ClientsAdminPage(Page, culture);
        await clients.OpenAsync();
        await clients.CreateAsync(SchoolGroup(names));
        await SignOutAsync();

        await InvitedAdminSignsInAsync(names.AdminEmail("school"), names.NewPassword);

        // The new admin's only assignment is selected automatically and the client-wide menu is there.
        var shell = new ShellPage(Page);
        await Expect(shell.Nav("orgunits")).ToBeVisibleAsync();
        await Expect(shell.Nav("leveltypes")).ToBeVisibleAsync();
        await Expect(shell.ContextSwitcher).ToContainTextAsync(culture == "ar" ? names.SchoolGroupAr : names.SchoolGroupEn);
        await AssertDirectionAsync(culture);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task School_admin_builds_level_types_and_tree(string culture)
    {
        var names = M1Names.New();
        var school = SchoolGroup(names);
        await NewClientAdminSignsInAsync(culture, school, names);

        var levels = new LevelTypesAdminPage(Page, culture);
        await levels.OpenAsync();
        await levels.AddAsync("مرحلة", "Stage", canHostClasses: false);
        await levels.AddAsync("فصل", "Section", canHostClasses: true);
        string[] expected = culture == "ar" ? ["مدرسة", "مرحلة", "فصل"] : ["School", "Stage", "Section"];
        Assert.Equal(expected, await levels.NamesInOrderAsync());

        // School → KG (transfer) → Section 1, 2; Primary (transfer) → Section 1..3.
        var root = school.RootCode;
        string Pick(string ar, string en) => culture == "ar" ? ar : en;
        var tree = new OrgTreeAdminPage(Page);
        await tree.OpenAsync();
        await tree.AddChildAsync(root, "رياض الأطفال", "KG", $"{root}-KG", Pick("مرحلة", "Stage"));
        await tree.AddChildAsync(root, "المرحلة الابتدائية", "Primary", $"{root}-PRI", Pick("مرحلة", "Stage"));
        foreach (var i in new[] { 1, 2 })
        {
            await tree.AddChildAsync($"{root}-KG", $"الفصل {i}", $"Section {i}", $"{root}-KG-S{i}", Pick("فصل", "Section"));
        }

        foreach (var i in new[] { 1, 2, 3 })
        {
            await tree.AddChildAsync($"{root}-PRI", $"الفصل {i}", $"Section {i}", $"{root}-PRI-S{i}", Pick("فصل", "Section"));
        }

        Assert.Equal(root, await tree.ParentCodeAsync($"{root}-KG"));
        Assert.Equal(root, await tree.ParentCodeAsync($"{root}-PRI"));
        Assert.Equal($"{root}-KG", await tree.ParentCodeAsync($"{root}-KG-S2"));
        Assert.Equal($"{root}-PRI", await tree.ParentCodeAsync($"{root}-PRI-S3"));
        await AssertDirectionAsync(culture);
    }

    /// <summary>The level-transfer flags endpoint (PUT /api/org-units/{id}/academic-flags) arrives with BE PR #148.</summary>
    private const string NeedsAcademicFlags = "Needs PUT /api/org-units/{id}/academic-flags (#148, not on main yet)";

    [Theory(Skip = NeedsAcademicFlags)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Stages_marked_as_level_transfer_show_the_transfer_path(string culture)
    {
        var names = M1Names.New();
        var school = SchoolGroup(names);
        await NewClientAdminSignsInAsync(culture, school, names);
        var levels = new LevelTypesAdminPage(Page, culture);
        await levels.OpenAsync();
        await levels.AddAsync("مرحلة", "Stage", canHostClasses: false);

        var root = school.RootCode;
        string Pick(string ar, string en) => culture == "ar" ? ar : en;
        var tree = new OrgTreeAdminPage(Page);
        await tree.OpenAsync();
        await tree.AddChildAsync(root, "رياض الأطفال", "KG", $"{root}-KG", Pick("مرحلة", "Stage"));
        await tree.AddChildAsync(root, "المرحلة الابتدائية", "Primary", $"{root}-PRI", Pick("مرحلة", "Stage"));

        // Both stages are level-transfer units; KG's next level is Primary.
        var primaryId = await tree.UnitIdAsync($"{root}-PRI");
        var kgId = await tree.UnitIdAsync($"{root}-KG");
        var academic = new UnitAcademicPage(Page, culture);
        await academic.OpenAsync(primaryId);
        await academic.MarkTransferAsync();
        await academic.OpenAsync(kgId);
        await academic.MarkTransferAsync(nextLevelName: Pick("المرحلة الابتدائية", "Primary"));

        await academic.OpenAsync(kgId);
        await Expect(academic.TransferPath).ToHaveTextAsync(Pick("رياض الأطفال ← المرحلة الابتدائية", "KG → Primary"));
        await AssertDirectionAsync(culture);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task University_admin_builds_faculty_tree_and_moves_a_specialization(string culture)
    {
        var names = M1Names.New();
        var university = University(names);
        await NewClientAdminSignsInAsync(culture, university, names, role: "uni");

        var levels = new LevelTypesAdminPage(Page, culture);
        await levels.OpenAsync();
        await levels.AddAsync("كلية", "Faculty", canHostClasses: false);
        await levels.AddAsync("سنة", "Year", canHostClasses: false);
        await levels.AddAsync("تخصص", "Specialization", canHostClasses: true);

        // University › FCI › First → general, Soft, Bio; Second → general.
        var root = university.RootCode;
        var fci = $"{root}-FCI";
        string Pick(string ar, string en) => culture == "ar" ? ar : en;
        var tree = new OrgTreeAdminPage(Page);
        await tree.OpenAsync();
        await tree.AddChildAsync(root, "كلية الحاسبات والمعلومات", "FCI", fci, Pick("كلية", "Faculty"));
        await tree.AddChildAsync(fci, "الفرقة الأولى", "First", $"{fci}-1", Pick("سنة", "Year"));
        await tree.AddChildAsync(fci, "الفرقة الثانية", "Second", $"{fci}-2", Pick("سنة", "Year"));
        await tree.AddChildAsync($"{fci}-1", "عام", "general", $"{fci}-1-GEN", Pick("تخصص", "Specialization"));
        await tree.AddChildAsync($"{fci}-1", "برمجيات", "Soft", $"{fci}-1-SOFT", Pick("تخصص", "Specialization"));
        await tree.AddChildAsync($"{fci}-1", "معلوماتية حيوية", "Bio", $"{fci}-1-BIO", Pick("تخصص", "Specialization"));
        await tree.AddChildAsync($"{fci}-2", "عام", "general", $"{fci}-2-GEN", Pick("تخصص", "Specialization"));
        Assert.Equal($"{fci}-1", await tree.ParentCodeAsync($"{fci}-1-BIO"));

        await tree.MoveAsync($"{fci}-1-BIO", Pick("الفرقة الثانية", "Second"));

        Assert.Equal($"{fci}-2", await tree.ParentCodeAsync($"{fci}-1-BIO"));
        Assert.Equal($"{fci}-1", await tree.ParentCodeAsync($"{fci}-1-SOFT"));
        await AssertDirectionAsync(culture);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Delete_unit_with_children_is_refused_with_message(string culture)
    {
        // The seeded school's KG stage has two sections; deleting it must be refused and change nothing.
        await StartAsync(culture: culture);
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        await new ShellPage(Page).WaitReadyAsync();

        var tree = new OrgTreeAdminPage(Page);
        await tree.OpenAsync();
        await tree.DeleteAsync("KG");

        await Expect(tree.Error).ToContainTextAsync(Resx.In(culture, "Error_unit_has_children"));
        await tree.OpenAsync();
        Assert.Equal("KG", await tree.ParentCodeAsync("KG-S1"));
        await AssertDirectionAsync(culture);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Seeded_org_admin_adds_reorders_and_deletes_a_level_type(string culture)
    {
        // Runs on main today (#81 page + #83 API): the seeded school client's admin, so no new client is needed.
        // The test removes its own level again, so the seeded School › Stage › Section stays as it was.
        var names = M1Names.New();
        var (nameAr, nameEn) = ($"مستوى {names.Suffix}", $"Level {names.Suffix}");
        var name = culture == "ar" ? nameAr : nameEn;
        await StartAsync(culture: culture);
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        await new ShellPage(Page).WaitReadyAsync();
        await AssertDirectionAsync(culture);

        var levels = new LevelTypesAdminPage(Page, culture);
        await levels.OpenAsync();
        string[] seeded = culture == "ar" ? ["مدرسة", "مرحلة", "فصل"] : ["School", "Stage", "Section"];
        Assert.Equal(seeded, (await levels.NamesInOrderAsync()).Take(3));

        await levels.AddAsync(nameAr, nameEn, canHostClasses: false);
        Assert.Equal(name, (await levels.NamesInOrderAsync())[^1]);

        await levels.MoveUpAsync(name);
        var reordered = await levels.NamesInOrderAsync();
        Assert.Equal(name, reordered[^2]);

        await levels.DeleteAsync(name);
        Assert.DoesNotContain(name, await levels.NamesInOrderAsync());
        Assert.Equal(seeded, (await levels.NamesInOrderAsync()).Take(3));
    }

    private static ClientsAdminPage.NewClient SchoolGroup(M1Names n) => new(
        n.SchoolGroupAr, n.SchoolGroupEn, "SchoolGroup", "مدرسة", "School", n.RootCode("SCH"),
        "مدير المدرسة", "School Admin", n.AdminEmail("school"));

    private static ClientsAdminPage.NewClient University(M1Names n) => new(
        n.UniversityAr, n.UniversityEn, "University", "جامعة", "University", n.RootCode("UNI"),
        "مدير الجامعة", "University Admin", n.AdminEmail("uni"));

    /// <summary>Platform admin creates the client; its invited first admin sets a password and signs in.</summary>
    private async Task NewClientAdminSignsInAsync(string culture, ClientsAdminPage.NewClient client, M1Names names, string role = "school")
    {
        await StartAsync(culture: culture);
        await SignInAsPlatformAdminAsync();
        var clients = new ClientsAdminPage(Page, culture);
        await clients.OpenAsync();
        await clients.CreateAsync(client);
        await SignOutAsync();
        await InvitedAdminSignsInAsync(names.AdminEmail(role), names.NewPassword);
    }

    private async Task SignInAsPlatformAdminAsync()
    {
        await new LoginPage(Page).SignInAsync(Settings.PlatformAdminEmail, Settings.Password);
        await new ShellPage(Page).WaitReadyAsync();
    }

    private async Task InvitedAdminSignsInAsync(string email, string password)
    {
        using var mail = new MailpitClient();
        var link = await mail.WaitForAccountLinkAsync(email);
        await new ResetPasswordPage(Page).SetPasswordAsync(link, password);
        await new LoginPage(Page).SignInAsync(email, password);
        await new ShellPage(Page).WaitReadyAsync();
    }

    /// <summary>Signs out of the app and the identity server so the next sign-in starts clean.</summary>
    private async Task SignOutAsync()
    {
        await Page.Context.ClearCookiesAsync();
        await Page.EvaluateAsync("() => { sessionStorage.clear(); localStorage.removeItem('lms.context'); }");
    }

    private async Task AssertDirectionAsync(string culture) =>
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
}
