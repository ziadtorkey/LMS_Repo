using Lms.E2ETests.Infrastructure;

namespace Lms.E2ETests.M6;

/// <summary>
/// DRAFT (QA1, M6 plan Task 33, exit demo 1: targeted announcements). Skipped until the M6 delivery and announcement backend (Tasks 5, 6, 8, 9, 13, 14, 18, 25, 26, 30) is on main.
/// Seeding helpers in <c>E2EApi</c> (a teacher of one class course, two students and a guardian, a manager, a second class) do not exist yet; they come with the first scenario that can run.
/// All run in ar and en; Arabic asserts <c>dir="rtl"</c>. Take <c>e2e.lock</c> before any run.
/// </summary>
public sealed class AnnouncementTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    private const string Pending = "M6 announcement backend not on main; seeding helpers come with the first live scenario";

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Teacher_announces_to_own_class_course_and_students_see_it(string culture) => Todo(culture);

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Teacher_cannot_target_a_class_they_do_not_teach(string culture) => Todo(culture); // the target is not offered; the API answers 404 for a forged one

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Manager_schedules_an_announcement_with_guardians_and_it_appears_at_publish_time(string culture) => Todo(culture); // fake clock; client zone shown on the form

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Course_page_lists_board_posts_and_announcements_once(string culture) => Todo(culture); // L15: merged read, each marked with its source

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Student_outside_the_audience_and_a_student_author_see_nothing_to_edit(string culture) => Todo(culture); // 404 on a foreign id, no Manage tab for a student

    private static Task Todo(string culture) => Task.FromException(new NotImplementedException($"scenario not written yet ({culture})"));
}
