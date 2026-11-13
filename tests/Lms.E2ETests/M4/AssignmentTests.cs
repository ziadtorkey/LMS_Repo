using Lms.E2ETests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.M4;

/// <summary>
/// M4a exit demo 2, assignments (plan Task 32, #382): a student hands in work after the deadline and the teacher grades it,
/// in Arabic (RTL) and English. The class course, lesson, teacher and student are created through the API; the hand-in and
/// the grading are driven through the UI. The late policy is judged on the server clock, so the deadline is set in the past.
/// </summary>
public sealed class AssignmentTests(E2EFixture fixture) : M4TestBase(fixture)
{
    private const string NeedsM4a = "Needs the FE assignment pages (#400, #512) and the quiz BE/FE (#453, #514, #519, #520, #521) on main; passes on a local integration of them. The blocked-late scenario already runs on main (#410)";

    private const decimal MaxGrade = 100;

    private async Task<Guid> CreateAssignmentAsync(Infrastructure.E2EApi admin, AssessmentKit.World world, string latePolicy, int? penalty)
    {
        var created = await admin.JsonAsync(await admin.SendAsync("POST", $"/api/class-courses/{world.ClassCourseId}/assignments", new
        {
            lessonKey = world.LessonKey,
            module = ModuleSettings("واجب القراءة", "Reading assignment", "Submit"),
            instructionsHtmlAr = "<p>اكتب ملخصًا.</p>",
            instructionsHtmlEn = "<p>Write a summary.</p>",
            attachmentAssetIds = Array.Empty<Guid>(),
            dueAtUtc = DateTime.UtcNow.AddHours(-2), // already past: a hand-in is late
            maxGrade = MaxGrade,
            latePolicy,
            penaltyPercent = penalty,
            resubmissionRule = "None",
            allowText = true,
            allowFiles = false,
        }));
        return created.GetProperty("id").GetGuid();
    }

    [Theory(Skip = NeedsM4a)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Teacher_publishes_assignment_student_submits_late_with_penalty_teacher_grades_with_annotated_file(string culture)
    {
        var (world, admin, names) = await StartWorldAsync(culture);
        var assignmentId = await CreateAssignmentAsync(admin, world, "Penalty", penalty: 20);
        await using var student = await OpenStudentAsync(world, names, culture);
        await using var teacher = await OpenTeacherAsync(culture);
        var path = $"/class-courses/{world.ClassCourseId}/assignments/{assignmentId}";

        // The student hands in after the deadline: the page says it is late and the work is stored.
        await student.Page.GotoAsync(path);
        await student.Page.GetByTestId("assignment-title").WaitForAsync();
        await Expect(student.Page.GetByTestId("assignment-late-note")).ToBeVisibleAsync();
        await student.Page.Locator("[data-testid='submit-text'] .ql-editor").FillAsync("My summary of the chapter");
        await SubmitWhenEditorHasSyncedAsync(student.Page);
        await Expect(student.Page.GetByTestId("assignment-handed-in")).ToContainTextAsync(Resx.In(culture, "AssignmentStudent_WasLate"));

        // The teacher finds the hand-in marked late, grades 80 with an annotated file and returns it.
        await teacher.Page.GotoAsync($"/course-assignments/{assignmentId}/submissions");
        await teacher.Page.GetByTestId("submissions-header").WaitForAsync();
        await Expect(teacher.Page.Locator("[data-testid^='late-']")).ToHaveCountAsync(1);
        await teacher.Page.Locator("[data-testid^='grade-']").First.ClickAsync();
        await Expect(teacher.Page.GetByTestId("grade-late")).ToBeVisibleAsync();
        await teacher.Page.GetByTestId("grade-input").Locator("xpath=descendant-or-self::input") /* MudBlazor puts the test id on the input itself */.FillAsync("80");
        await Expect(teacher.Page.GetByTestId("grade-penalty")).ToBeVisibleAsync(); // the 20 % penalty is shown before saving
        var annotated = Path.Combine(Directory.CreateTempSubdirectory("lms-e2e-annot-").FullName, "annotated.txt");
        await File.WriteAllTextAsync(annotated, "Well structured; add a conclusion.");
        await teacher.Page.GetByTestId("grade-annotated").Locator("input[type='file']").SetInputFilesAsync(annotated);
        await Expect(teacher.Page.GetByTestId("grade-annotated-name")).ToContainTextAsync("annotated.txt");
        await teacher.Page.GetByTestId("grade-return").ClickAsync();
        await Expect(teacher.Page.GetByTestId("grade-status")).ToContainTextAsync(Resx.In(culture, "SubmissionStatus_Returned"));

        // The student sees the returned grade: 80 less 20 % = 64 of 100, with the penalty named.
        await student.Page.ReloadAsync();
        await Expect(student.Page.GetByTestId("assignment-final-grade")).ToContainTextAsync("64");
        await Expect(student.Page.GetByTestId("assignment-final-grade")).ToContainTextAsync("100");
        await Expect(student.Page.GetByTestId("assignment-penalty")).ToBeVisibleAsync();
        await AssertDirectionAsync(student.Page, culture);
        await AssertDirectionAsync(teacher.Page, culture);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Blocked_late_assignment_refuses_submission(string culture)
    {
        var (world, admin, names) = await StartWorldAsync(culture);
        var assignmentId = await CreateAssignmentAsync(admin, world, "Block", penalty: null);
        await using var student = await OpenStudentAsync(world, names, culture);

        // The page offers no form, and the server refuses a hand-in made around it.
        await student.Page.GotoAsync($"/class-courses/{world.ClassCourseId}/assignments/{assignmentId}");
        await student.Page.GetByTestId("assignment-title").WaitForAsync();
        await Expect(student.Page.GetByTestId("submit-blocked")).ToBeVisibleAsync();
        await Expect(student.Page.GetByTestId("submit-panel")).ToHaveCountAsync(0);
        var api = student.Api(Settings.BaseUrl);
        var response = await api.SendAsync("POST", $"/api/course-assignments/{assignmentId}/submit", new { textHtml = "<p>Too late</p>", fileAssetIds = Array.Empty<Guid>(), saveAsDraft = false });
        Assert.Equal(409, response.Status);
        await AssertDirectionAsync(student.Page, culture);
    }

    /// <summary>
    /// The rich-text editor sends its text to the page after a short debounce (and on blur), so a Submit clicked the instant the text
    /// was typed can run before the page has it and answers "write your answer first". Click Submit again until the hand-in is accepted.
    /// </summary>
    private static async Task SubmitWhenEditorHasSyncedAsync(IPage page)
    {
        var handedIn = page.GetByTestId("assignment-handed-in");
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await page.GetByTestId("submit-send").ClickAsync();
            try
            {
                await handedIn.WaitForAsync(new() { Timeout = 3_000 });
                return;
            }
            catch (TimeoutException)
            {
                // The page did not have the text yet; try again.
            }
        }

        await Expect(handedIn).ToBeVisibleAsync();
    }
}
