using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Lms.E2ETests.M1.M1Kit;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M1;

/// <summary>M1 exit demo 3 (plan Task 38): academic years, terms, calendar inheritance (FR-YEAR-01..03, D22) and the year selector (X-04).</summary>
public sealed class YearsTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    /// <summary>
    /// The root admin defines a 2026/27 year with two terms and a holiday on a stage (fresh units per run, so the suite can
    /// run again on the same database), and the section below it inherits all of it read-only.
    /// </summary>
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Root_admin_defines_2026_year_with_two_terms_and_a_holiday_school_inherits_it(string culture)
    {
        await StartAsync(test: $"{nameof(Root_admin_defines_2026_year_with_two_terms_and_a_holiday_school_inherits_it)}-{culture}");
        await SignInAsync(Page, E2ESettings.SeedUser("orgadmin"), Settings.Password, culture);
        var run = RunId();
        string stageAr = $"مرحلة {run}", stageEn = $"Stage {run}", sectionAr = $"فصل {run}", sectionEn = $"Section {run}";

        var tree = new OrgTreePage(Page);
        await tree.GotoAsync();
        var stageId = await tree.AddChildAsync(SchoolName(culture), stageAr, stageEn, $"ST{run}", StageLevel(culture));
        var sectionId = await tree.AddChildAsync(Pick(culture, stageAr, stageEn), sectionAr, sectionEn, $"SE{run}", SectionLevel(culture));

        // Define the year on the stage: two terms and a holiday.
        var years = new YearsPage(Page);
        await years.GotoAsync(stageId);
        string yearAr = $"العام 2026/2027 {run}", yearEn = $"Year 2026/2027 {run}";
        var yearId = await years.DefineYearAsync(yearAr, yearEn, new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 30), Pick(culture, yearAr, yearEn));
        await years.AddTermAsync(yearId, "الفصل الأول", "Term 1", new DateOnly(2026, 9, 1), new DateOnly(2027, 1, 31));
        await years.AddTermAsync(yearId, "الفصل الثاني", "Term 2", new DateOnly(2027, 2, 1), new DateOnly(2027, 6, 30));
        await years.AddEventAsync(yearId, $"عطلة {run}", $"Holiday {run}", Text(culture, "Calendar_Holiday"),
            new DateOnly(2027, 1, 7), new DateOnly(2027, 1, 7));

        await Expect(years.Terms(yearId)).ToContainTextAsync(Pick(culture, "الفصل الأول", "Term 1"));
        await Expect(years.Terms(yearId)).ToContainTextAsync(Pick(culture, "الفصل الثاني", "Term 2"));
        await Expect(years.Calendar(yearId)).ToContainTextAsync(Pick(culture, $"عطلة {run}", $"Holiday {run}"));

        // The section inherits the stage's year, terms and holiday, and says where they are defined.
        await years.GotoAsync(sectionId);
        await Expect(years.Inherited).ToContainTextAsync(
            string.Format(Text(culture, "Years_DefinedAt"), Pick(culture, stageAr, stageEn)));
        await Expect(years.Year(Pick(culture, yearAr, yearEn))).ToHaveCountAsync(1);
        await Expect(Page.GetByTestId($"year-edit-{yearId}")).ToHaveCountAsync(0); // read-only here
        await Expect(years.Terms(yearId)).ToContainTextAsync(Pick(culture, "الفصل الثاني", "Term 2"));
        var holiday = years.Calendar(yearId).Locator("[data-testid^='event-inherited-']");
        await Expect(holiday).ToContainTextAsync(
            string.Format(Text(culture, "Calendar_InheritedFrom"), Pick(culture, stageAr, stageEn)));
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await Page.Locator("html").GetAttributeAsync("dir"));
    }

    /// <summary>
    /// The app-bar selector shows the years of the active context (orgadmin at the school root) and starts on the Active
    /// one; a choice is kept per context across reloads. The two school years are created once and reused by later runs.
    /// </summary>
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Year_selector_defaults_to_active_year_and_switches(string culture)
    {
        await StartAsync(test: $"{nameof(Year_selector_defaults_to_active_year_and_switches)}-{culture}");
        await SignInAsync(Page, E2ESettings.SeedUser("orgadmin"), Settings.Password, culture);
        var (active, planned) = await EnsureSchoolYearsAsync(culture);

        var select = Page.GetByTestId("year-select");
        var shown = select; // MudSelect renders the selected item as text inside the field
        await Expect(shown).ToContainTextAsync(active);

        await MudControls.SelectAsync(Page, select, planned);
        await Expect(shown).ToContainTextAsync(planned);

        await Page.ReloadAsync();
        await new ShellPage(Page).WaitReadyAsync();
        await Expect(shown).ToContainTextAsync(planned);
        await Expect(Page.GetByTestId("year-lock")).ToHaveCountAsync(0); // only Closed years are read-only
    }

    /// <summary>Makes sure the school root has an Active 2027/28 and a Planned 2028/29 year; returns their names in <paramref name="culture"/>.</summary>
    private async Task<(string Active, string Planned)> EnsureSchoolYearsAsync(string culture)
    {
        string activeAr = "العام E2E 2027/2028", activeEn = "E2E Year 2027/2028", plannedAr = "العام E2E 2028/2029", plannedEn = "E2E Year 2028/2029";
        var tree = new OrgTreePage(Page);
        await tree.GotoAsync();
        var schoolId = await tree.IdOfAsync(SchoolName(culture));

        var years = new YearsPage(Page);
        await years.GotoAsync(schoolId);
        var active = Pick(culture, activeAr, activeEn);
        var planned = Pick(culture, plannedAr, plannedEn);
        if (await years.Year(active).CountAsync() == 0)
        {
            var id = await years.DefineYearAsync(activeAr, activeEn, new DateOnly(2027, 9, 1), new DateOnly(2028, 6, 30), active);
            await years.ActivateAsync(id);
        }

        if (await years.Year(planned).CountAsync() == 0)
        {
            await years.DefineYearAsync(plannedAr, plannedEn, new DateOnly(2028, 9, 1), new DateOnly(2029, 6, 30), planned);
        }

        // The selector loads its list once per context; reload so it sees years created just now.
        await Page.ReloadAsync();
        await new ShellPage(Page).WaitReadyAsync();
        return (active, planned);
    }
}
