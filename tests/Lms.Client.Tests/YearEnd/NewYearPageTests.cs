using System.Globalization;
using Bunit;
using Lms.Client.Components.People;
using Lms.Client.Pages.AcademicYears;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Services.YearEnd;
using Lms.Client.Tests.Support;
using Lms.Shared.AcademicYears;
using Lms.Shared.Authorization;
using Lms.Shared.YearEnd;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.YearEnd;

/// <summary>Open the next year from a copy or a template (M7 H6; FR-YEAR-05, FR-ORG-07/08, D22).</summary>
public sealed class NewYearPageTests : ClientTestContext
{
    private readonly FakeNewYearApi _newYear = new();

    public NewYearPageTests()
    {
        Services.AddSingleton<INewYearApi>(_newYear);
        Services.AddSingleton<IUnitLookupApi, FakeUnitLookupApi>();
        Culture.Current = new CultureInfo("en");
        FakeCultureService.Apply(new CultureInfo("en"));
    }

    private async Task<AcademicYearDto> ActiveYearAsync() => (await YearsApi.GetEffectiveAsync(null)).Years.First(y => y.Status == AcademicYearStatuses.Active);

    private async Task<IRenderedComponent<NewYearPage>> OpenAsync(bool allowed = true, bool slow = false)
    {
        _newYear.Slow = slow;
        if (allowed) await GrantAsync(Permissions.AcademicYears.Create);
        else await GrantAsync(Permissions.AcademicYears.View);
        Render<MudPopoverProvider>();
        var year = await ActiveYearAsync();
        var cut = Render<NewYearPage>(p => p.Add(x => x.YearId, year.Id).Add(x => x.PollEvery, TimeSpan.FromMilliseconds(5)).Add(x => x.AnnounceEvery, TimeSpan.Zero));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=newyear-loading]")));
        return cut;
    }

    private static async Task NextAsync(IRenderedComponent<NewYearPage> cut)
    {
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid=newyear-next]").HasAttribute("disabled")));
        await cut.Find("[data-testid=newyear-next]").ClickAsync(new());
    }

    [Fact]
    public async Task Without_the_permission_the_page_says_so()
    {
        var cut = await OpenAsync(allowed: false);

        Assert.NotEmpty(cut.FindAll("[data-testid=newyear-forbidden]"));
        Assert.Empty(cut.FindAll("[data-testid=newyear-dates]"));
    }

    [Fact]
    public async Task It_suggests_the_next_name_and_dates_and_shows_the_terms_with_shifted_dates()
    {
        var cut = await OpenAsync();

        Assert.Equal("step", cut.Find("[data-testid=newyear-step-0]").GetAttribute("aria-current"));
        Assert.Contains("2027/2028", cut.Markup);
        Assert.Contains("2027-09-01", cut.Find("[data-testid=newyear-terms]").TextContent); // term 1 starts the day the year does, a year later
        Assert.Equal(new DateTime(2027, 9, 1), cut.FindComponents<MudDatePicker>().First().Instance.Date); // the dates move a year on
    }

    [Theory]
    [InlineData("2026/2027", "2027/2028")]
    [InlineData("٢٠٢٦/٢٠٢٧", "٢٠٢٧/٢٠٢٨")]
    [InlineData("Founders year", "Founders year")]
    [InlineData("2026", "2027")]
    public void The_next_name_moves_every_year_number_on(string name, string expected)
    {
        Assert.Equal(expected, NewYearPage.NextName(name));
    }

    [Fact]
    public async Task The_structure_step_defaults_to_a_copy_says_students_are_never_copied_and_needs_a_part()
    {
        var cut = await OpenAsync();
        await NextAsync(cut);

        Assert.NotEmpty(cut.FindAll("[data-testid=newyear-structure]"));
        Assert.Contains("never copied", cut.Find("[data-testid=newyear-never-students]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=newyear-template]"));
        foreach (var part in new[] { YearCopyParts.Classes, YearCopyParts.ClassCourses })
            await cut.InvokeAsync(() => cut.FindComponents<MudCheckBox<bool>>().First(c => Equals(c.Instance.UserAttributes["data-testid"], $"newyear-part-{part}")).Instance.ValueChanged.InvokeAsync(false));

        cut.WaitForAssertion(() => Assert.True(cut.Find("[data-testid=newyear-next]").HasAttribute("disabled"))); // nothing to copy: no way forward
    }

    [Fact]
    public async Task A_template_mode_asks_for_the_template_and_where_to_create_it_and_hides_the_copy_parts()
    {
        var cut = await OpenAsync();
        await NextAsync(cut);

        await cut.InvokeAsync(() => cut.FindComponents<MudRadioGroup<string>>().Single().Instance.ValueChanged.InvokeAsync("template"));

        cut.WaitForElement("[data-testid=newyear-template]");
        Assert.NotEmpty(cut.FindComponents<UnitPicker>());
        Assert.Empty(cut.FindAll("[data-testid=newyear-parts]"));
        Assert.True(cut.Find("[data-testid=newyear-next]").HasAttribute("disabled")); // no template and no parent picked yet
    }

    [Fact]
    public async Task Review_shows_what_would_be_created_and_then_the_job_ends_with_links_to_go_on()
    {
        var cut = await OpenAsync();
        await NextAsync(cut); // to the structure
        await NextAsync(cut); // to the review

        cut.WaitForElement("[data-testid=newyear-review]");
        Assert.Contains("8", cut.Find("[data-testid=newyear-counts]").TextContent);
        Assert.Contains("40", cut.Find("[data-testid=newyear-counts]").TextContent);
        Assert.Equal("Create the year", cut.Find("[data-testid=newyear-next]").TextContent.Trim());

        await NextAsync(cut); // create

        cut.WaitForElement("[data-testid=newyear-result]");
        Assert.Contains("created as planned", cut.Find("[data-testid=newyear-result]").TextContent);
        Assert.Equal("promotion", cut.Find("[data-testid=newyear-start-promotion]").GetAttribute("href"));
        Assert.Equal("academic-years", cut.Find("[data-testid=newyear-open-years]").GetAttribute("href"));
    }

    [Fact]
    public async Task A_running_copy_shows_progress_until_it_is_advanced()
    {
        var cut = await OpenAsync(slow: true);
        await NextAsync(cut);
        await NextAsync(cut);
        await NextAsync(cut);

        cut.WaitForElement("[data-testid=newyear-progress]");
        Assert.Empty(cut.FindAll("[data-testid=newyear-result]"));
        Assert.Equal("polite", cut.Find("[data-testid=job-announcer]").GetAttribute("aria-live"));

        _newYear.Advance(_newYear.LastRunId!.Value, 1000);

        cut.WaitForElement("[data-testid=newyear-result]");
    }

    [Fact]
    public async Task Overlapping_dates_send_the_person_back_to_the_dates_with_the_reason()
    {
        _newYear.ExistingYears.Add((new DateOnly(2027, 8, 1), new DateOnly(2028, 8, 1)));
        var cut = await OpenAsync();
        await NextAsync(cut);

        await cut.Find("[data-testid=newyear-next]").ClickAsync(new());

        cut.WaitForElement("[data-testid=newyear-error]");
        Assert.NotEmpty(cut.FindAll("[data-testid=newyear-dates]")); // back on the dates step
        Assert.Equal("step", cut.Find("[data-testid=newyear-step-0]").GetAttribute("aria-current"));
    }

    [Fact]
    public async Task Back_returns_to_the_previous_step_and_clears_the_review()
    {
        var cut = await OpenAsync();
        await NextAsync(cut);
        await NextAsync(cut);
        cut.WaitForElement("[data-testid=newyear-review]");

        await cut.Find("[data-testid=newyear-back]").ClickAsync(new());

        Assert.NotEmpty(cut.FindAll("[data-testid=newyear-structure]"));
        Assert.Empty(cut.FindAll("[data-testid=newyear-review]"));
    }

    [Fact]
    public async Task A_missing_year_shows_an_error_not_a_crash()
    {
        await GrantAsync(Permissions.AcademicYears.Create);

        var cut = Render<NewYearPage>(p => p.Add(x => x.YearId, Guid.NewGuid()));

        cut.WaitForElement("[data-testid=newyear-load-error]");
    }

    [Fact]
    public async Task The_years_page_offers_the_entry_for_an_own_year_with_the_permission()
    {
        await GrantAsync(Permissions.AcademicYears.View, Permissions.AcademicYears.Create);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        Render<MudPopoverProvider>();
        Render<MudDialogProvider>();

        var page = Render<AcademicYearsPage>();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid^=year-open-next-]")));
        Assert.EndsWith("/open-next", page.Find("[data-testid^=year-open-next-]").GetAttribute("href"));
    }

    [Fact]
    public async Task The_page_renders_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        FakeCultureService.Apply(new CultureInfo("ar"));

        var cut = await OpenAsync();

        Assert.Contains("التواريخ", cut.Markup);
        Assert.DoesNotContain("NewYear_", cut.Markup);
    }

    [Fact]
    public void New_year_resources_exist_in_both_languages()
    {
        foreach (var culture in new[] { "ar", "en" })
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var l = Services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<Lms.Client.Resources.SharedResource>>();
            string[] keys = [.. YearCopyParts.All.Select(p => $"NewYear_Part_{p}"), "NewYear_Title", "NewYear_Open", "NewYear_Never_Students", "NewYear_Done", "NewYear_Failed", "NewYear_Create", "NewYear_Skipped_Item"];
            Assert.All(keys, k => Assert.False(l[k].ResourceNotFound, $"{culture}:{k}"));
        }
    }
}
