using Lms.E2ETests.Infrastructure;

namespace Lms.E2ETests.M5;

/// <summary>Unique names per test run.</summary>
internal static class M5Names
{
    public static string Suffix() => Guid.NewGuid().ToString("N")[..6];
}

/// <summary>
/// The class, course, teachers, students and sessions of the M5 timetable and attendance demos. DRAFT shape: the ids the scenarios need,
/// filled by <see cref="M5Kit.BuildWorldAsync"/> once the M5 routes exist.
/// </summary>
public sealed record M5World(
    Guid SchoolId, Guid ClassAId, Guid ClassBId, string CourseName, string TeacherName, string TeacherEmail, string SubstituteEmail,
    string CounselorEmail, Guid SessionId, Guid[] StudentIds, string GuardianUserName, string GuardianPassword);

/// <summary>
/// Set-up for the M5 demos through the API (nothing the scenarios are not testing is clicked). Like <c>M4Kit</c> (#435) for the sign-in
/// helpers (kept apart until #435 puts <c>M4Kit</c> on main: then <c>SignInAsync</c>/<c>SignOutAsync</c> are deleted here and the M4 ones called); the world needs the M5 contract routes, so the builder is a documented stub until Tasks 13 to 17 are on main.
/// </summary>
public static class M5Kit
{
    /// <summary>
    /// To build, in order, as the signed-in org admin: the year and two classes (M3 routes), a course with a class course in each class and a
    /// teacher for both (the clash), a second teacher as the substitute and a counselor, three or more enrolled students, a guardian linked to
    /// the first student (with <c>Features.GuardianPortal</c> enabled and the account activated, as the M3 demo does), periods, the timetable
    /// conflict mode, and, by flags: slots (<paramref name="withSlots"/>), the timetable published so sessions exist (<paramref name="published"/>),
    /// an exception that makes the substitute teach the test date (<paramref name="substituteToday"/>), a taken session whose edit window has
    /// closed (<paramref name="takenAndWindowClosed"/>) and a taken session with one absence (<paramref name="takenWithAbsence"/>).
    /// <para>
    /// <b>Never depend on the runner's day.</b> Set <b>all seven weekdays</b> as working days (Saudi Sunday to Thursday would make a Friday or
    /// Saturday run generate nothing) and derive "today" from the server's own time in the client zone (<c>serverNowUtc</c> of a list response,
    /// Asia/Riyadh, D64), never from <c>DateTime.Today</c>; put the slot on that weekday and the exception on that date.
    /// </para>
    /// <para>
    /// <b>Edit window:</b> set the setting <c>Attendance.EditWindowHours</c> (L14) to its minimum through the settings API before the take, so the
    /// window is closed when the scenario looks at it; there is no server clock offset in the E2E stack.
    /// </para>
    /// The routes are the wave 2 and 3 constants of plan #621 (periods, working days, timetable, exceptions, attendance); use <c>Routes.*</c>
    /// once they are on main.
    /// </summary>
    public static Task<M5World> BuildWorldAsync(
        ApiSetup api, string suffix, string conflictMode, bool withSlots, bool published, bool substituteToday, bool takenAndWindowClosed,
        bool takenWithAbsence) =>
        throw new NotImplementedException("Needs the M5 contract wave 2 and 3 routes (plan #621 Tasks 13 to 17) on main.");

    /// <summary>Forgets the session (browser storage and cookies) so the next sign-in is a different person.</summary>
    public static async Task SignOutAsync(Microsoft.Playwright.IPage page)
    {
        await page.EvaluateAsync("() => { sessionStorage.clear(); localStorage.clear(); }");
        await page.Context.ClearCookiesAsync();
    }

    /// <summary>
    /// Signs in (through the forced password change a new account has, D24), waits for the shell and the stored context, and switches
    /// the UI to English when asked.
    /// </summary>
    public static async Task SignInAsync(Microsoft.Playwright.IPage page, string userName, string password, string culture)
    {
        await new Lms.E2ETests.Pages.LoginPage(page).SignInAsync(userName, password);
        if (page.Url.Contains("/Account/ChangePassword", StringComparison.OrdinalIgnoreCase))
        {
            await page.GetByTestId("change-current").FillAsync(password);
            await page.GetByTestId("change-new").FillAsync(password + "x1");
            await page.GetByTestId("change-confirm").FillAsync(password + "x1");
            await page.GetByTestId("change-submit").ClickAsync();
            await page.WaitForURLAsync(url => !url.Contains("/Account/", StringComparison.OrdinalIgnoreCase)
                                              && !url.Contains("/authentication/", StringComparison.OrdinalIgnoreCase));
        }

        var shell = new Lms.E2ETests.Pages.ShellPage(page);
        await shell.WaitReadyAsync();
        await page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
        if (culture == "en" && await shell.HtmlLangAsync() != "en")
        {
            await shell.SwitchLanguageAsync();
        }
    }
}
