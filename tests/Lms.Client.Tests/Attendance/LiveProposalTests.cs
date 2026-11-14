using System.Globalization;
using Bunit;
using Lms.Client.Pages.Attendance;
using Lms.Client.Services.Attendance;
using Lms.Client.Tests.Support;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Attendance;

public sealed class LiveProposalTests : StaffTestContext
{
    private static readonly Guid Session = FakeAttendanceApi.SessionProposal;
    private static readonly Guid Ahmed = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid Layla = Guid.Parse("50000000-0000-0000-0000-000000000004");
    private static readonly Guid GuestA = Guid.Parse("b0000000-0000-0000-0000-00000000000a");
    private static readonly Guid GuestB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private readonly FakeAttendanceApi _api = new();
    private readonly StorageJs _js = new();

    public LiveProposalTests()
    {
        Services.AddSingleton<IAttendanceApi>(_api);
        Services.AddSingleton(new AttendanceDraftStore(_js, TimeProvider.System));
    }

    private async Task<IRenderedComponent<AttendanceTakePage>> RenderAsync(string culture = "en")
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(Permissions.Attendance.Take);
        RenderProviders();
        var page = Render<AttendanceTakePage>(p => p.Add(x => x.Id, Session));
        page.WaitForElement("[data-testid=att-roster]");
        return page;
    }

    [Fact]
    public async Task Proposal_banner_and_confirm_replace_save()
    {
        var historyBefore = (await _api.GetHistoryAsync(Session)).Count;
        var page = await RenderAsync();

        Assert.Contains("proposed from the Zoom participants list", page.Find("[data-testid=proposal-banner]").TextContent);
        Assert.Contains("Confirm attendance", page.Find("[data-testid=att-save]").TextContent); // Confirm takes the place of Save

        page.Find("[data-testid=att-save]").Click();

        page.WaitForElement("[data-testid=att-saved]");
        Assert.Equal(AttendanceSessionStates.Taken, _api.Sessions.Single(s => s.Id == Session).State);
        Assert.False(_api.Sessions.Single(s => s.Id == Session).IsProposal); // nothing proposed any more
        Assert.Equal(historyBefore, (await _api.GetHistoryAsync(Session)).Count); // an unchanged proposal is confirmed as it is, not re-taken
        Assert.Empty(page.FindAll("[data-testid=proposal-banner]"));
    }

    [Fact]
    public async Task An_edited_proposal_is_taken_with_the_teachers_marks_and_settles_it()
    {
        var page = await RenderAsync();
        page.Find($"[data-testid=att-{Layla}-Absent]").Click();

        page.Find("[data-testid=att-save]").Click();

        page.WaitForElement("[data-testid=att-saved]");
        Assert.False(_api.Sessions.Single(s => s.Id == Session).IsProposal);
        Assert.Contains(await _api.GetHistoryAsync(Session), c => c.StudentId == Layla && c.ToStatus == AttendanceStatuses.Absent);
    }

    [Fact]
    public async Task Source_hint_has_text_for_assistive_technology()
    {
        var page = await RenderAsync();

        var hint = page.Find($"[data-testid=proposal-hint-{Ahmed}]");

        Assert.Contains("In Zoom 42 min", hint.TextContent); // words on the row, not only an icon
        Assert.Contains("In Zoom 42 min", hint.GetAttribute("title"));
    }

    [Fact]
    public async Task Unmatched_guests_can_be_mapped_or_skipped()
    {
        var page = await RenderAsync();
        Assert.Contains("iPhone 12", page.Find($"[data-testid=unmatched-{GuestA}]").TextContent);
        Assert.Contains("Mona's mum", page.Find($"[data-testid=unmatched-{GuestB}]").TextContent);

        await ChooseAsync(SelectWithTestId(page, $"unmatched-select-{GuestA}"), (Guid?)Ahmed); // map one, leave the other empty (skip)
        page.Find("[data-testid=unmatched-save]").Click();

        page.WaitForAssertion(() => Assert.Contains("lms-att-selected", page.Find($"[data-testid=att-{Ahmed}-Present]").ClassName));
        var records = await _api.GetRecordsAsync(Session);
        Assert.Equal(AttendanceStatuses.Present, records.Records.Single(r => r.StudentId == Ahmed).Status);
        Assert.Equal(Ahmed, records.Unmatched.Single(u => u.ParticipantId == GuestA).MappedStudentId);
        Assert.Null(records.Unmatched.Single(u => u.ParticipantId == GuestB).MappedStudentId); // skipped
    }

    [Fact]
    public async Task Participants_not_available_yet_shows_the_warning_and_blank_rows()
    {
        _api.ParticipantsMissing = true;
        var page = await RenderAsync();

        Assert.Contains("has not returned the participant list", page.Find("[data-testid=proposal-unavailable]").TextContent);
        Assert.Empty(page.FindAll("[data-testid=proposal-banner]"));
    }

    [Fact]
    public async Task A_session_that_is_not_a_proposal_shows_none_of_it()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Attendance.Take);
        RenderProviders();
        var page = Render<AttendanceTakePage>(p => p.Add(x => x.Id, FakeAttendanceApi.SessionOpen));
        page.WaitForElement("[data-testid=att-roster]");

        Assert.Empty(page.FindAll("[data-testid=proposal-banner], [data-testid=unmatched]"));
        Assert.Contains("Save attendance", page.Find("[data-testid=att-save]").TextContent);
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        var page = await RenderAsync("ar");

        Assert.Contains("اقتُرحت", page.Find("[data-testid=proposal-banner]").TextContent);
        Assert.Contains("تأكيد الحضور", page.Find("[data-testid=att-save]").TextContent);
        Assert.Contains("في Zoom", page.Find($"[data-testid=proposal-hint-{Ahmed}]").TextContent);
    }
}
