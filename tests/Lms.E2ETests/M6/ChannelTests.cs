using Lms.E2ETests.Infrastructure;

namespace Lms.E2ETests.M6;

/// <summary>
/// DRAFT (QA1, M6 plan Task 33, exit demo 1: delivery on the user's channels). Skipped until the M6 delivery backend (Tasks 5, 6, 9, 14, 18, 25, 26) is on main. Uses the
/// development fake outbox (<c>/api/dev/comms/outbox</c>, <c>/api/dev/comms/script</c>) to see what was "sent"; no SMS exists in v1 (D76), so every scenario that reads the
/// outbox also asserts it holds no <c>Sms</c> row. Uses <c>NotificationPreferencesPage</c> (QA3). All in ar and en; take <c>e2e.lock</c> before any run.
/// </summary>
public sealed class ChannelTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    private const string Pending = "M6 delivery backend not on main; the fake outbox helper in E2EApi comes with the first live scenario";

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Fake_email_outbox_holds_one_email_per_recipient_in_their_language(string culture) => Todo(culture);

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Guardian_who_switched_email_off_gets_in_app_only(string culture) => Todo(culture); // also: a row held before the switch is skipped, not sent (#750 send-time check)

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Mandatory_event_cannot_be_switched_off(string culture) => Todo(culture); // the cell is locked; the API answers 409 notification_mandatory

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Push_subscription_round_trip_against_the_fake_push_endpoint(string culture) => Todo(culture); // permission asked only after a click; consent sentence recorded server side

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Guardian_gets_absence_and_announcements_by_email_and_push(string culture) => Todo(culture); // D72; the outbox holds no SMS row for anyone (D76); push text has no student data

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Urgent_announcement_bypasses_quiet_hours_on_push(string culture) => Todo(culture);

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Quiet_hours_hold_push_and_release_it_in_the_morning(string culture) => Todo(culture); // fake clock, client zone

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Preferences_page_offers_no_sms_column_and_no_sms_setting(string culture) => Todo(culture); // D76

    private static Task Todo(string culture) => Task.FromException(new NotImplementedException($"scenario not written yet ({culture})"));
}
