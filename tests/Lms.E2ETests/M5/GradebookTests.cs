using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M5;

/// <summary>
/// M5 exit demo 1 (plan #621 Task 42; FR-GRD-01..05): a teacher sets weights, enters a manual score and publishes; the student and the guardian see exactly
/// the published grade; a hidden item is invisible to both (whole-page leak check, docs/security/m5-review.md section 1); a scheduled item appears after
/// its time. Arabic (RTL) and English. The flows are written against #647, #650, #656, #660, #662, #663, #664 and FE1's id map (docs/m5-e2e-testids.md,
/// #710); they have not run yet, so every scenario is skipped (each with the PRs it waits for). Where the publish dialog (Task 27) is not on main the
/// API publishes and the step is marked.
/// </summary>
public sealed class GradebookTests(E2EFixture fixture) : M5TestBase(fixture)
{
    private const string NotImplemented = "scenario not implemented yet: see the steps in the body";

    [Theory(Skip = NeedsGradebookGrid + " and the publish dialog (#721) on main")]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Teacher_sets_weights_enters_a_manual_score_and_publishes(string culture)
    {
        var (world, admin, _) = await StartWorldAsync(culture);
        await using var teacher = await OpenTeacherAsync(culture);
        var gradebook = new GradebookPage(teacher.Page);
        await gradebook.OpenAsync(world.ClassCourseId);
        await AssertDirectionAsync(teacher.Page, culture);

        // Weights through the settings panel (Task 26): Homework 40 and Exams 60; after the save the chip shows the server's total.
        await gradebook.OpenSettingsAsync();
        await gradebook.AddCategory.ClickAsync();
        await gradebook.CategoryName(0, "ar").FillAsync("واجبات");
        await gradebook.CategoryName(0, "en").FillAsync("Homework");
        await gradebook.CategoryWeight(0).FillAsync("40");
        await gradebook.AddCategory.ClickAsync();
        await gradebook.CategoryName(1, "ar").FillAsync("اختبارات");
        await gradebook.CategoryName(1, "en").FillAsync("Exams");
        await gradebook.CategoryWeight(1).FillAsync("60");
        await gradebook.SaveCategories.ClickAsync();
        await Expect(gradebook.WeightsChip).ToContainTextAsync("100");

        // A manual item through the item dialog, then a score typed into its cell (Enter, value, Enter).
        await gradebook.AddItem.ClickAsync();
        await gradebook.ItemTitle("ar").FillAsync("اختبار قصير");
        await gradebook.ItemTitle("en").FillAsync("Quiz 1");
        await gradebook.ItemMax.FillAsync("10");
        await teacher.Page.GetByTestId("dialog-save").ClickAsync();
        var item = await M5Kit.ItemIdAsync(admin, world.ClassCourseId, "Quiz 1");
        await teacher.Page.Keyboard.PressAsync("Escape"); // close the settings panel
        await gradebook.TypeScoreAsync(world.Student.Id, item, "8");

        // The teacher's own grid already shows the server-computed final; the item is still hidden from students.
        await Expect(gradebook.Final(world.Student.Id)).ToContainTextAsync("80");
        await Expect(gradebook.ItemState(item)).ToBeVisibleAsync();

        // Publish through the dialog (Task 27, #721). Closing it, by Esc or by Done, must return the focus to the Publish button (WCAG 2.4.3): asserted
        // for real here because the bUnit test of #721 can only check the static markup.
        var dialog = new PublishItemsDialog(teacher.Page);
        await gradebook.PublishButton.FocusAsync();
        await gradebook.PublishButton.ClickAsync();
        await Expect(dialog.Submit).ToBeVisibleAsync();
        await teacher.Page.Keyboard.PressAsync("Escape");
        await Expect(dialog.Submit).ToHaveCountAsync(0);
        await Expect(gradebook.PublishButton).ToBeFocusedAsync();

        await gradebook.PublishButton.ClickAsync();
        await dialog.PublishNowAsync(item);
        await Expect(gradebook.PublishButton).ToBeFocusedAsync();
        var book = await M5Kit.GradebookAsync(admin, world.ClassCourseId);
        Assert.Equal("Published", book.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == item).GetProperty("publishState").GetString());
    }

    [Theory(Skip = NeedsPublishing)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Student_sees_the_published_item_and_total(string culture)
    {
        var (world, admin, names) = await StartWorldAsync(culture);
        var item = await M5Kit.CreateManualItemAsync(admin, world.ClassCourseId, "اختبار قصير", "Quiz 1", 10);
        await M5Kit.SetScoreAsync(admin, world.ClassCourseId, world.Student.Id, item, 8);
        await M5Kit.PublishAsync(admin, world.ClassCourseId, [item]);
        await using var student = await OpenStudentAsync(world, names, culture);

        var grades = new MyGradesPage(student.Page);
        await grades.OpenAsync();

        await Expect(grades.Item(item)).ToContainTextAsync("8");
        await Expect(grades.Percent(world.ClassCourseId)).ToContainTextAsync("80");
        await Expect(grades.Basis(world.ClassCourseId)).ToBeVisibleAsync(); // "based on published grades"
        await Expect(student.Page.Locator("[data-testid^='grades-item-']")).ToHaveCountAsync(1);
        await AssertDirectionAsync(student.Page, culture);
    }

    [Theory(Skip = NeedsPublishing + ", and the guardian portal flag set in the world")]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Guardian_sees_the_same_published_grade(string culture)
    {
        var (world, admin, names, school) = await StartWorldWithPortalAsync(culture);
        try
        {
            var item = await M5Kit.CreateManualItemAsync(admin, world.ClassCourseId, "اختبار قصير", "Quiz 1", 10);
            await M5Kit.SetScoreAsync(admin, world.ClassCourseId, world.Student.Id, item, 8);
            await M5Kit.PublishAsync(admin, world.ClassCourseId, [item]);
            await using var guardian = await OpenGuardianAsync(names, culture);

            var grades = new MyGradesPage(guardian.Page);
            await grades.OpenChildAsync(world.Student.Id);
            await Expect(grades.Item(item)).ToContainTextAsync("8");
            await Expect(grades.Percent(world.ClassCourseId)).ToContainTextAsync("80");

            // Another family's (here: an unknown) child answers like an unknown id and shows no grades.
            await guardian.Page.GotoAsync($"/me/children/{Guid.NewGuid()}/grades");
            await Expect(grades.StudentName).ToHaveCountAsync(0);
        }
        finally
        {
            await M5Kit.SetGuardianPortalAsync(admin, school, on: false);
        }
    }

    [Theory(Skip = NeedsPublishing + ", and the guardian portal flag set in the world")]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Unpublished_item_is_invisible_to_student_and_guardian(string culture)
    {
        var (world, admin, names, school) = await StartWorldWithPortalAsync(culture);
        try
        {
            var shown = await M5Kit.CreateManualItemAsync(admin, world.ClassCourseId, "ظاهر", "Visible test", 10);
            var marker = "Secret-" + names.Suffix;
            var secret = await M5Kit.CreateManualItemAsync(admin, world.ClassCourseId, marker, marker, 10);
            await M5Kit.SetScoreAsync(admin, world.ClassCourseId, world.Student.Id, shown, 8);
            await M5Kit.SetScoreAsync(admin, world.ClassCourseId, world.Student.Id, secret, 3);
            await M5Kit.PublishAsync(admin, world.ClassCourseId, [shown]);
            await using var student = await OpenStudentAsync(world, names, culture);
            await using var guardian = await OpenGuardianAsync(names, culture);
            var studentProbe = LeakProbe.Attach(student.Page);
            var guardianProbe = LeakProbe.Attach(guardian.Page);

            await new MyGradesPage(student.Page).OpenAsync();
            await Expect(new MyGradesPage(student.Page).Item(shown)).ToBeVisibleAsync();
            await new MyGradesPage(guardian.Page).OpenChildAsync(world.Student.Id);
            await Expect(new MyGradesPage(guardian.Page).Item(shown)).ToBeVisibleAsync();

            // Whole-page check: the HTML (attributes included), every API response with its headers, and the console never carry the hidden item.
            Assert.NotEmpty(studentProbe.Mentioning("/api/me/grades"));
            Assert.NotEmpty(guardianProbe.Mentioning($"/api/students/{world.Student.Id}/grades"));
            await studentProbe.AssertAbsentAsync(marker);
            await guardianProbe.AssertAbsentAsync(marker);
            await Expect(student.Page.GetByTestId($"grades-item-{secret}")).ToHaveCountAsync(0);
            await Expect(new MyGradesPage(student.Page).Percent(world.ClassCourseId)).ToContainTextAsync("80"); // the total ignores the hidden 3/10
        }
        finally
        {
            await M5Kit.SetGuardianPortalAsync(admin, school, on: false);
        }
    }

    [Theory(Skip = "Needs the M4a quiz and grading merge (#453, #421) and the grade-event observer of the gradebook (#638) on main")]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Quiz_score_appears_as_a_hidden_auto_item(string culture)
    {
        // Steps: a published quiz in the class course (the API set-up of M4/QuizTests); the student takes and submits it; the teacher's grid shows an auto
        // item for it in state Hidden with the chip "stays hidden until you publish" (L3) and the student sees nothing; after the publish the student
        // sees the quiz item and the updated total.
        Assert.Fail($"{NotImplemented} ({culture})");
        return Task.CompletedTask;
    }

    [Theory(Skip = NeedsPublishing + "; there is no dev trigger for the scheduled-publish job, so this relies on the real per-minute job")]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Scheduled_item_appears_after_the_time(string culture)
    {
        var (world, admin, names) = await StartWorldAsync(culture);
        var item = await M5Kit.CreateManualItemAsync(admin, world.ClassCourseId, "اختبار مجدول", "Scheduled quiz", 10);
        await M5Kit.SetScoreAsync(admin, world.ClassCourseId, world.Student.Id, item, 9);
        await using var student = await OpenStudentAsync(world, names, culture);
        var grades = new MyGradesPage(student.Page);
        await M5Kit.PublishAsync(admin, world.ClassCourseId, [item], "Schedule", DateTime.UtcNow.AddSeconds(75));

        await grades.OpenAsync();
        await Expect(grades.Item(item)).ToHaveCountAsync(0); // before its time the student sees nothing

        // The real job runs every minute: reload until the item shows (up to ~3 minutes), with no fixed sleep.
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (await grades.Item(item).CountAsync() == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "the scheduled item did not appear within 3 minutes");
            try
            {
                await grades.Item(item).WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
            }
            catch (TimeoutException)
            {
                await student.Page.ReloadAsync();
                await Expect(grades.Title).ToBeVisibleAsync();
            }
        }

        await Expect(grades.Item(item)).ToHaveCountAsync(1); // exactly once
    }
}
