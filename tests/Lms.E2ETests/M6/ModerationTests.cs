using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.Pages;
using static Microsoft.Playwright.Assertions;

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
    public async Task Guardian_reports_a_message_as_a_safety_concern_and_it_tops_the_queue(string culture)
    {
        var w = await ModerationWorld.SeedAsync();
        await StartAsync(culture: culture);
        await SignInAsync(w.GuardianEmail, w.Password);
        var thread = new ThreadPage(Page);
        await thread.OpenAsync(w.ConversationId);
        var report = new ReportDialog(Page);
        await report.ReportAsync(w.MessageId, "SafetyConcern", "E2E-NOTE");
        await Expect(report.ReportedChip(w.MessageId)).ToBeVisibleAsync(); // the flag is replaced by the reported chip
        await SignOutAsync();

        await SignInAsync(w.OrgAdminEmail, w.Password);
        var queue = new ModerationQueuePage(Page);
        await queue.OpenAsync();
        await Expect(queue.Rows.First).ToHaveAttributeAsync("data-safety", "True");
        await AssertDirectionAsync(culture);
    }

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Org_admin_sees_only_the_frozen_context_and_cannot_open_the_conversation(string culture)
    {
        var w = await ModerationWorld.SeedAsync();
        var reportId = await ReportAsGuardianAsync(w, culture);
        await SignInAsync(w.OrgAdminEmail, w.Password);
        var queue = new ModerationQueuePage(Page);
        await queue.OpenAsync();
        await queue.OpenReportAsync(reportId);
        await Expect(queue.FrozenContext).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("mod-note")).ToHaveCountAsync(1);

        // The live thread is not reachable for the moderator: the same page a missing conversation gives.
        await Page.GotoAsync($"/messages/{w.ConversationId}");
        await Expect(Page.GetByTestId("thread-body")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("messages-unavailable")).ToBeVisibleAsync();
    }

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Remove_and_restrict_tombstones_the_message_and_blocks_sending_for_7_days(string culture)
    {
        var w = await ModerationWorld.SeedAsync();
        var reportId = await ReportAsGuardianAsync(w, culture);
        await SignInAsync(w.OrgAdminEmail, w.Password);
        var queue = new ModerationQueuePage(Page);
        await queue.OpenAsync();
        await queue.OpenReportAsync(reportId);
        await queue.DecideAsync("RemoveAndRestrict", "E2E-NOTE-TO-PEOPLE", days: 7);
        await Expect(queue.Resolved).ToBeVisibleAsync();
        await SignOutAsync();

        // The sender (the teacher) can read but not send: the composer is off with the restriction reason.
        await SignInAsync(w.TeacherEmail, w.Password);
        var thread = new ThreadPage(Page);
        await thread.OpenAsync(w.ConversationId);
        await Expect(thread.Message(w.MessageId)).Not.ToContainTextAsync("E2E-ORIGINAL-TEXT"); // tombstone: no text served
        await Expect(thread.Composer).ToHaveCountAsync(0);
    }

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
    public async Task Student_blocks_a_user_and_the_blocked_side_sees_only_read_only(string culture)
    {
        var w = await ModerationWorld.SeedAsync();
        await StartAsync(culture: culture);
        await SignInAsync(w.StudentEmail, w.Password);
        var thread = new ThreadPage(Page);
        await thread.OpenAsync(w.ConversationId);
        await thread.BlockAsync();
        await Expect(thread.ComposerUnblock).ToBeVisibleAsync(); // the blocker's composer offers "unblock" in place of the box
        await SignOutAsync();

        // The teacher is never told: the thread reads as read-only, with no word of a block.
        await SignInAsync(w.TeacherEmail, w.Password);
        await thread.OpenAsync(w.ConversationId);
        await Expect(thread.Composer).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("thread-body")).Not.ToContainTextAsync(Resx.In(culture, "Msg_Blocked_ByMe"));
    }

    [Theory(Skip = Pending)]
    [InlineData("ar")]
    [InlineData("en")]
    public Task Moderator_cannot_see_or_lift_a_user_block(string culture) => Todo(culture);

    private static Task Todo(string culture) => Task.FromException(new NotImplementedException($"scenario not written yet ({culture})"));

    private async Task SignOutAsync()
    {
        await Page.Context.ClearCookiesAsync();
        await Page.EvaluateAsync("() => { sessionStorage.clear(); localStorage.removeItem('lms.context'); }");
    }

    private async Task SignInAsync(string email, string password)
    {
        await new LoginPage(Page).SignInAsync(email, password);
        var shell = new ShellPage(Page);
        await shell.WaitReadyAsync();
        await Expect(shell.ContextSwitcher).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("() => !!localStorage.getItem('lms.context')");
    }

    private async Task AssertDirectionAsync(string culture) =>
        Assert.Equal(culture == "ar" ? "rtl" : "ltr", await new ShellPage(Page).HtmlDirAsync());

    /// <summary>Starts the culture, signs in as the guardian, reports the world's message as a safety concern, signs out and returns the new report id (read from the API).</summary>
    private async Task<Guid> ReportAsGuardianAsync(ModerationWorld w, string culture)
    {
        await StartAsync(culture: culture);
        await SignInAsync(w.GuardianEmail, w.Password);
        var thread = new ThreadPage(Page);
        await thread.OpenAsync(w.ConversationId);
        await new ReportDialog(Page).ReportAsync(w.MessageId, "SafetyConcern");
        await SignOutAsync();
        throw new NotImplementedException("read the new report id from the API (E2EApi) once the moderation routes are on main");
    }
}
