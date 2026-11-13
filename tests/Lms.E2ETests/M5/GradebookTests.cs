using Lms.E2ETests.Infrastructure;

namespace Lms.E2ETests.M5;

/// <summary>
/// M5 exit demo 1 (plan #621 Task 42; FR-GRD-01..05): a teacher sets weights, enters a manual score and publishes; the student and the
/// guardian see exactly the published grade; a hidden item is invisible to both; a quiz score shows up as a hidden auto item; a scheduled
/// item appears after its time. Arabic (RTL) and English. SCAFFOLD: every scenario is skipped until the features land; each body lists
/// the steps to implement (UI-driven; set-up through the API only).
/// </summary>
public sealed class GradebookTests(E2EFixture fixture) : M5TestBase(fixture)
{
    [Theory(Skip = NeedsGradebook)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Teacher_sets_weights_enters_a_manual_score_and_publishes(string culture)
    {
        // Set-up (API): class course, teacher of record, 2 students, one guardian linked to student 1.
        // UI (teacher): open the class course Gradebook tab; set category weights (Homework 40, Exams 60; the weights chip shows 100 and the
        // grid is valid); add a manual item "Quiz 1" (max 10) to Homework; enter 8 for student 1; open Publish, choose Now, confirm.
        // Assert: the cell shows 8; the publish result names 1 affected student; the item state is Published; direction is rtl/ltr per culture.
        await Task.CompletedTask;
        Assert.NotNull(culture);
    }

    [Theory(Skip = NeedsGradebook)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Student_sees_the_published_item_and_total(string culture)
    {
        // Precondition: the item above is published. UI (student, second browser context): open My grades.
        // Assert: the course lists "Quiz 1" 8/10; the category and final percent equal the engine result over published items only;
        // the band and the band legend show as configured; no rank or class statistics anywhere; direction per culture.
        await Task.CompletedTask;
        Assert.NotNull(culture);
    }

    [Theory(Skip = NeedsGradebook)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Guardian_sees_the_same_published_grade(string culture)
    {
        // UI (guardian): open the child's grades. Assert: the same item, points, category, final and band as the student sees;
        // the second guardian-less child's id in the URL answers the same not-found page as an unknown id.
        await Task.CompletedTask;
        Assert.NotNull(culture);
    }

    [Theory(Skip = NeedsGradebook)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Unpublished_item_is_invisible_to_student_and_guardian(string culture)
    {
        // Teacher adds a second manual item "Secret test" (title with a unique marker) and scores it, but does not publish it.
        // Assert as student and as guardian: the title marker and its score appear nowhere on the page (text search of the whole body);
        // the total is unchanged; the item count equals the published count; an API read (E2EApi GET /api/me/grades) holds no marker and no
        // hidden count; the staff "student view" preview shows exactly what the student sees. Mirrors docs/security/m5-review.md section 1.
        await Task.CompletedTask;
        Assert.NotNull(culture);
    }

    [Theory(Skip = NeedsGradebook)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Quiz_score_appears_as_a_hidden_auto_item(string culture)
    {
        // Set-up (API): a published quiz in the class course; the student takes and submits it (API or UI of the M4a demo).
        // UI (teacher): the gradebook shows an auto item for the quiz in state Hidden with the chip "stays hidden until you publish" (L3);
        // the student sees nothing yet; the teacher publishes it; the student then sees the quiz item and the updated total.
        await Task.CompletedTask;
        Assert.NotNull(culture);
    }

    [Theory(Skip = NeedsGradebook)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Scheduled_item_appears_after_the_time(string culture)
    {
        // Teacher schedules an item for a moment about 60 s ahead by the server clock (serverNowUtc). Before it: the student sees nothing.
        // Trigger the scheduled-publish job (dev trigger, as the quiz-close job in M4a) once the time has passed; poll the student page until the
        // item shows (no fixed sleep). Assert it appears exactly once and the guardian sees it too.
        await Task.CompletedTask;
        Assert.NotNull(culture);
    }

    [Theory(Skip = NeedsGradebook)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Grid_is_operable_by_keyboard_only(string culture)
    {
        // Keyboard and screen-reader pass of the grid (L24): arrows move the active cell; Enter or F2 starts editing; Tab moves right only while
        // editing; Esc cancels; Shift+F10 opens the cell menu; the focused cell is never under the sticky header/column (scroll-padding);
        // accessibility tree: each cell has a name (student, item) and state (Event, Manual, Override, Excused, Empty); saves announce in one polite
        // live region and a rejected value once, assertively. Direction per culture.
        await Task.CompletedTask;
        Assert.NotNull(culture);
    }
}
