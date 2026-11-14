using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.M4;

namespace Lms.E2ETests.M5;

/// <summary>
/// Shared set-up of the M5 exit demos (plan #621 Tasks 42-44). It reuses the M4a world (<see cref="AssessmentKit"/>: school, class course with a
/// published lesson plan, the seeded teacher assigned, an enrolled student) and the session helpers of <see cref="M4TestBase"/> (a second browser
/// context per role, the Org Admin acting as the grader until the E2E year is current for the seeded teacher, #455); M5 adds <see cref="M5Kit"/>.
/// </summary>
public abstract class M5TestBase(E2EFixture fixture) : M4TestBase(fixture)
{
    /// <summary>Skip reason of the scenarios that need the whole gradebook (grid, entries, publishing, reads).</summary>
    protected const string NeedsGradebook =
        "Needs the M5 gradebook: contract (#625), scales (#641), model and writer (#638), grid and publishing (Tasks 8, 11) and the FE pages (Tasks 24-27, 29, 39) on main";

    /// <summary>Skip reason of the scenarios that need only the gradebook tab shell and the grading scales.</summary>
    protected const string NeedsShellAndScales = "Needs the gradebook tab (#630) and the grading scales API (#641, #625) on main";
}
