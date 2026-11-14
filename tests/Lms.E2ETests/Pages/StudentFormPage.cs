using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The create-student form (/students/new, FE Task 37), its credentials dialog and the student profile header.</summary>
public sealed class StudentFormPage(IPage page)
{
    public sealed record NewStudent(
        string NameAr, string NameEn, DateOnly DateOfBirth, string GenderText, string ReligionText, string NationalityText,
        string NationalId, string HomeUnitText, string HomeUnitAfter, string? ClassText = null, string? Email = null, string? Password = null);

    public sealed record NewGuardian(string NameAr, string NameEn, string NationalId, string Mobile, string? Email = null);

    public ILocator Error => page.GetByTestId("student-error");
    public ILocator Snackbar => page.Locator(".mud-snackbar");
    public ILocator CredentialsDialog => page.Locator(".mud-dialog").Filter(new LocatorFilterOptions { Has = page.GetByTestId("credentials-code") });

    public async Task OpenAsync()
    {
        await page.GotoAsync("/students/new");
        await page.GetByTestId("student-create-title").WaitForAsync();
    }

    /// <summary>Opens a student's profile by URL (the app reloads, so wait for the page to render).</summary>
    public async Task OpenProfileAsync(Guid id)
    {
        await page.GotoAsync($"/students/{id}");
        await page.GetByTestId("profile-code").WaitForAsync();
    }

    /// <summary>Fills the personal fields, home unit and optional class/e-mail/password. The guardian rows are filled separately.</summary>
    public async Task FillAsync(NewStudent s)
    {
        await Pickers.PickAsync(page, "student-home-unit", s.HomeUnitText, s.HomeUnitAfter);
        await FillTextAsync("student-name-ar", s.NameAr);
        await FillTextAsync("student-name-en", s.NameEn);
        await PickDateOfBirthAsync(s.DateOfBirth);
        await Pickers.PickAsync(page, "student-gender", s.GenderText);
        await Pickers.PickAsync(page, "student-religion", s.ReligionText);
        await Pages.MudControls.AutocompleteAsync(page, page.GetByTestId("student-nationality"), s.NationalityText, s.NationalityText);
        await FillTextAsync("student-national-id", s.NationalId);
        if (s.Email is not null) await FillTextAsync("student-email", s.Email);
        if (s.ClassText is not null) await Pickers.PickAsync(page, "student-class", s.ClassText);
        if (s.Password is not null) await FillTextAsync("student-password", s.Password);
    }

    /// <summary>Fills a new (not yet matched) guardian row; the first row starts as the primary father, which is what the scenarios want.</summary>
    public async Task FillNewGuardianAsync(int index, NewGuardian g)
    {
        if (index > 0 || await page.GetByTestId("guardian-national-id-0").CountAsync() == 0)
        {
            await page.GetByTestId("guardian-add").ClickAsync();
        }

        await FillTextAsync($"guardian-national-id-{index}", g.NationalId);
        await FillTextAsync($"guardian-mobile-{index}", g.Mobile);
        await FillTextAsync($"guardian-name-ar-{index}", g.NameAr);
        await FillTextAsync($"guardian-name-en-{index}", g.NameEn);
        if (g.Email is not null) await FillTextAsync($"guardian-email-{index}", g.Email);
    }

    /// <summary>Saves; the result is either the credentials dialog, the duplicate prompt or an error on the page.</summary>
    public Task SaveAsync() => page.GetByTestId("student-save").ClickAsync();

    public async Task CloseCredentialsAsync()
    {
        await page.GetByTestId("credentials-done").ClickAsync();
        await page.GetByTestId("students-title").WaitForAsync();
    }

    /// <summary>The input of a text field: MudTextField puts <c>data-testid</c> on the input itself or on a wrapper around it.</summary>
    public ILocator Field(string testId) =>
        page.Locator($"input[data-testid='{testId}'], [data-testid='{testId}'] input, [data-testid='{testId}'] textarea").First;

    private Task FillTextAsync(string testId, string value) => Field(testId).FillAsync(value);

    /// <summary>
    /// The date field opens on the year list (OpenTo.Year): pick the year, then the month, then the day.
    /// </summary>
    private async Task PickDateOfBirthAsync(DateOnly date)
    {
        await page.Locator("input[data-testid='student-dob'], [data-testid='student-dob'] input").First.ClickAsync();
        var picker = page.Locator(".mud-picker-open").Last;
        await picker.WaitForAsync();
        var years = picker.Locator(".mud-picker-year");
        if (await years.First.IsVisibleAsync())
        {
            await years.Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{date.Year}\s*$") }).First.ClickAsync();
        }

        var months = picker.Locator(".mud-picker-month");
        if (await months.First.IsVisibleAsync())
        {
            await months.Nth(date.Month - 1).ClickAsync();
        }

        await picker.Locator("button.mud-picker-calendar-day:not(.mud-adjacent-month):not(.mud-hidden)")
            .Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{date.Day}\s*$") }).First.ClickAsync();
        await page.GetByTestId("student-create-title").ClickAsync(); // close the popover
    }
}
