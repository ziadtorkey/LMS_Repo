using Lms.E2ETests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M5;

/// <summary>
/// Shared set-up of the M5 exit demos (plan #621 Tasks 42-44). Scaffold: the world builder (school, class course with a published
/// blueprint, a teacher assigned and current, an enrolled student with a linked guardian, a quiz that produces a grade event) is added
/// when the gradebook API and pages land; it goes through the API, like <c>M3Kit</c>/<c>M4Kit</c> do, and everything the demo asserts is
/// driven through the UI.
/// </summary>
public abstract class M5TestBase : E2ETestBase
{
    /// <summary>Why the scenarios are skipped; update it (and remove it) as the features land.</summary>
    protected const string NeedsGradebook =
        "Needs the M5 gradebook: wave 1 contract (#625), engine and writer (Tasks 4-9), publishing (Task 11) and the FE pages (Tasks 22-27, 29, 39) on main";

    protected M5TestBase(E2EFixture fixture) : base(fixture)
    {
        Assertions.SetDefaultExpectTimeout(30_000); // the WASM app takes ~11 s to boot on a cold page
    }

    protected async Task AssertDirectionAsync(IPage page, string culture) =>
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await page.EvaluateAsync<string>("() => document.documentElement.dir"));
}
