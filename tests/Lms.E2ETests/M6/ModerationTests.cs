using Lms.E2ETests.Infrastructure;

namespace Lms.E2ETests.M6;

/// <summary>
/// DRAFT (QA3, M6 plan Task 35, exit demo 3: report, moderation, restriction and block). Skipped until the M6 messaging and moderation backend is on main.
/// Each test needs seeding helpers in <c>E2EApi</c> (a teacher, student and guardian of one class, a conversation with a message, a report) that do not exist yet;
/// they are written with the first scenario that can run. All run in ar and en; Arabic asserts <c>dir="rtl"</c>.
/// </summary>
public sealed class ModerationTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    private const string Pending = "M6 backend not on main; seeding helpers and the report dialog page object come with the first live scenario";

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Guardian_reports_a_message_as_a_safety_concern_and_it_tops_the_queue(string culture) => Todo(culture);

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Org_admin_sees_only_the_frozen_context_and_cannot_open_the_conversation(string culture) => Todo(culture);

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Remove_and_restrict_tombstones_the_message_and_blocks_sending_for_7_days(string culture) => Todo(culture);

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Reporter_sees_reviewed_and_sender_is_told_without_the_reporter(string culture) => Todo(culture);

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Sibling_school_admin_does_not_see_the_report(string culture) => Todo(culture);

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Lifting_the_restriction_restores_sending(string culture) => Todo(culture);

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Student_blocks_a_user_and_the_blocked_side_sees_only_read_only(string culture) => Todo(culture);

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Moderator_cannot_see_or_lift_a_user_block(string culture) => Todo(culture);

    private static Task Todo(string culture) => Task.FromException(new NotImplementedException($"scenario not written yet ({culture})"));
}
