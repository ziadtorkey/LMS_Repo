using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M1;

/// <summary>
/// M1 exit demo 2, configuration half (plan Task 37, #72): policies, locked settings and feature flags on the seeded
/// school client (S › KG › sections, S › Primary). Ar and en. Every test puts the shared seeded data back in a
/// <c>finally</c> block (policy off, value reset), so reruns and the other scenarios stay unaffected.
/// The API message of a blocked create is covered by <c>M1PolicyBlockProbeTests</c>; here the UI side is checked.
/// Copy branch and templates (plan scenario 3) follow with their pages (#207, #258).
/// </summary>
public sealed class ConfigurationTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    private const string MaxUpload = "Files.MaxUploadMb";
    private const string Messaging = "Features.Messaging";

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Root_admin_sets_CreateOrgUnits_on_KG_and_add_is_hidden_below_it(string culture)
    {
        await StartAsync(culture: culture);
        await SignInAsOrgAdminAsync();
        var tree = new OrgTreeAdminPage(Page);
        await tree.OpenAsync();
        var kg = await tree.RevealAsync("KG");
        var config = new UnitConfigPage(Page, culture);

        try
        {
            await config.OpenPoliciesAsync(kg);
            await config.SetPolicyAsync("CreateOrgUnits", on: true);

            await tree.OpenAsync();
            var kgSection = await tree.RevealAsync("KG-S1");
            var primary = await tree.RevealAsync("PRI");
            await Expect(Page.GetByTestId($"org-add-{kg}")).ToHaveCountAsync(0);        // the policy unit itself
            await Expect(Page.GetByTestId($"org-add-{kgSection}")).ToHaveCountAsync(0); // inherited below it
            await Expect(Page.GetByTestId($"org-add-{primary}")).ToBeVisibleAsync();     // a sibling is not affected

            // Below KG the policy shows as inherited from KG and cannot be switched off there.
            await config.OpenPoliciesAsync(kgSection);
            await Expect(Page.GetByTestId("policy-source-CreateOrgUnits")).ToBeVisibleAsync();
            await Expect(config.PolicySwitch("CreateOrgUnits")).ToBeDisabledAsync();
        }
        finally
        {
            await config.OpenPoliciesAsync(kg);
            await config.SetPolicyAsync("CreateOrgUnits", on: false);
        }

        await tree.OpenAsync();
        var section = await tree.RevealAsync("KG-S1");
        await Expect(Page.GetByTestId($"org-add-{section}")).ToBeVisibleAsync();
        await AssertDirectionAsync(culture);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Locked_root_setting_is_read_only_at_school_level(string culture)
    {
        await StartAsync(culture: culture);
        await SignInAsOrgAdminAsync();
        var tree = new OrgTreeAdminPage(Page);
        await tree.OpenAsync();
        var root = await tree.RevealAsync("S");
        var kg = await tree.RevealAsync("KG");
        var config = new UnitConfigPage(Page, culture);

        try
        {
            await config.OpenSettingsAsync(root);
            await config.SetNumberAsync(MaxUpload, 150);
            await config.LockAsync(MaxUpload);

            await config.OpenSettingsAsync(kg);
            await Expect(config.SettingInput(MaxUpload)).ToBeDisabledAsync();
            await Expect(config.SettingInput(MaxUpload)).ToHaveValueAsync("150");
            await Expect(config.SettingSource(MaxUpload)).ToContainTextAsync(Resx.In(culture, "Settings_LockedBy").Split("{0}")[0].Trim());
        }
        finally
        {
            await config.OpenSettingsAsync(root);
            await config.ResetAsync(MaxUpload); // removes the value and its lock
        }

        await AssertDirectionAsync(culture);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Feature_flag_enabled_at_KG_only_is_off_at_Primary(string culture)
    {
        await StartAsync(culture: culture);
        await SignInAsOrgAdminAsync();
        var tree = new OrgTreeAdminPage(Page);
        await tree.OpenAsync();
        var kg = await tree.RevealAsync("KG");
        var section = await tree.RevealAsync("KG-S1");
        var primary = await tree.RevealAsync("PRI");
        var config = new UnitConfigPage(Page, culture);

        try
        {
            await config.OpenSettingsAsync(kg);
            await Expect(config.SettingInput(Messaging)).Not.ToBeCheckedAsync();
            await config.ToggleFlagAsync(Messaging);
            await Expect(config.SettingInput(Messaging)).ToBeCheckedAsync();

            await config.OpenSettingsAsync(section);   // inherited by the sections below KG
            await Expect(config.SettingInput(Messaging)).ToBeCheckedAsync();
            await Expect(config.SettingSource(Messaging)).ToContainTextAsync(Resx.In(culture, "Settings_InheritedFrom").Split("{0}")[0].Trim());

            await config.OpenSettingsAsync(primary);   // the sibling stage keeps the default
            await Expect(config.SettingInput(Messaging)).Not.ToBeCheckedAsync();
            await Expect(config.SettingSource(Messaging)).ToContainTextAsync(Resx.In(culture, "Settings_Default"));
        }
        finally
        {
            await config.OpenSettingsAsync(kg);
            await config.ResetAsync(Messaging);
        }

        await AssertDirectionAsync(culture);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Copy_Primary_branch_under_the_school_with_a_code_suffix(string culture)
    {
        var suffix = "-c" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        await StartAsync(culture: culture);
        await SignInAsOrgAdminAsync();
        var tree = new OrgTreeAdminPage(Page);
        await tree.OpenAsync();

        var result = await tree.CopyBranchAsync(
            "PRI", culture == "ar" ? "مدرسة التجمع" : "School Altagamo3", suffix, "ابتدائي نسخة " + suffix, "Primary copy " + suffix);

        // The branch is the stage plus its three sections; each code is the original plus the suffix.
        Assert.Contains("4", result);
        await tree.OpenAsync();
        Assert.Equal("S", await tree.ParentCodeAsync("PRI" + suffix));
        Assert.Equal("PRI" + suffix, await tree.ParentCodeAsync("PRI-S1" + suffix));
        Assert.Equal("PRI" + suffix, await tree.ParentCodeAsync("PRI-S3" + suffix));
        await AssertDirectionAsync(culture);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Save_branch_as_template_with_a_Year_placeholder_then_apply_for_2027(string culture)
    {
        var sfx = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        string Pick(string ar, string en) => culture == "ar" ? ar : en;
        var (stageCode, sectionCode) = ($"G2026-{sfx}", $"G2026-{sfx}-A");
        var templateNameAr = $"قالب الصف {sfx}";
        var templateNameEn = $"Grade template {sfx}";
        await StartAsync(culture: culture);
        await SignInAsOrgAdminAsync();
        var tree = new OrgTreeAdminPage(Page);
        await tree.OpenAsync();

        // A stage with one section, both carrying "2026" in their names and codes.
        await tree.AddChildAsync("S", $"الصف 2026 {sfx}", $"Grade 2026 {sfx}", stageCode, Pick("مرحلة", "Stage"));
        await tree.AddChildAsync(stageCode, $"فصل 2026 {sfx}", $"Class 2026 {sfx}", sectionCode, Pick("فصل", "Section"));
        await tree.SaveAsTemplateAsync(stageCode, templateNameAr, templateNameEn, "2026", "Year");

        var templates = new TemplatesPage(Page);
        await templates.OpenAsync();
        await Expect(templates.Row(Pick(templateNameAr, templateNameEn))).ToContainTextAsync("{Year}");
        var result = await templates.ApplyAsync(
            Pick(templateNameAr, templateNameEn), Pick("مدرسة التجمع", "School Altagamo3"), new Dictionary<string, string> { ["Year"] = "2027" });

        // Both units are created below the school, with 2027 in place of the placeholder.
        Assert.Contains("2", result);
        await tree.OpenAsync();
        Assert.Equal("S", await tree.ParentCodeAsync($"G2027-{sfx}"));
        Assert.Equal($"G2027-{sfx}", await tree.ParentCodeAsync($"G2027-{sfx}-A"));
        await AssertDirectionAsync(culture);
    }

    private async Task SignInAsOrgAdminAsync()
    {
        await new LoginPage(Page).SignInAsync(E2ESettings.SeedUser("orgadmin"), Settings.Password);
        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();
        // The context is picked after the shell renders; opening a page before that answers 403 and shows "no access".
        await Expect(shell.ContextSwitcher).ToBeVisibleAsync();
    }

    private async Task AssertDirectionAsync(string culture) =>
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());
}
