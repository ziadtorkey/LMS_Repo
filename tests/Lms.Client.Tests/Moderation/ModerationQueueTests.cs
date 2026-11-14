using System.Globalization;
using Bunit;
using Lms.Client.Components.Messaging;
using Lms.Client.Pages.Moderation;
using Lms.Client.Services.Api;
using Lms.Client.Services.Messaging;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Moderation;

/// <summary>The moderation queue (M6 Task 29, FR-MSG-04, D66, D70, L16) on the fake: the Org Admin only, the frozen context only, an audited read, decisions that say what they do.</summary>
public sealed class ModerationQueueTests : ClientTestContext
{
    private static readonly DateTime T0 = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
    private readonly FakeModerationApi _api = new() { Now = () => T0 };

    public ModerationQueueTests() => Services.AddSingleton<IModerationApi>(_api);

    private async Task<IRenderedComponent<ModerationQueuePage>> OpenAsync(string culture = "en", params string[] permissions)
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        await GrantAsync(permissions.Length == 0 ? [Permissions.Messaging.Moderate] : permissions);
        Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
        var page = Render<ModerationQueuePage>();
        if (permissions.Length == 0) page.WaitForElement("[data-testid=mod-queue]");
        return page;
    }

    private static async Task<ApiProblemException> Refused(Func<Task> call) => await Assert.ThrowsAsync<ApiProblemException>(call);

    // ---- the fake mirrors the server rules

    [Fact]
    public async Task Only_the_org_admin_moderates_and_other_roles_get_403_or_zero_numbers()
    {
        _api.AddReport();
        _api.IsOrgAdmin = false;

        Assert.Equal(403, (await Refused(() => _api.ListReportsAsync(new MessageReportQuery()))).Status);
        Assert.Equal(403, (await Refused(() => _api.GetReportAsync(Guid.NewGuid()))).Status);
        Assert.Equal(0, (await _api.GetStatsAsync()).Open); // outside the scope the numbers are zero, never 404
    }

    [Fact]
    public async Task A_report_carries_the_frozen_context_and_no_more_and_never_the_reporter()
    {
        var id = _api.AddReport();

        var report = await _api.GetReportAsync(id);

        Assert.Equal(MessagingLimits.ReportContextEachSide * 2 + 1, report.Context.Count); // five each side plus the reported one
        Assert.Single(report.Context, c => c.IsReported);
        Assert.DoesNotContain("Reporter", string.Join(",", typeof(MessageReportDto).GetProperties().Select(p => p.Name))); // the DTO has no reporter field (D66)
    }

    [Fact]
    public async Task The_first_read_moves_open_to_in_review_and_every_read_is_audited()
    {
        var id = _api.AddReport();

        await _api.GetReportAsync(id);
        await _api.GetReportAsync(id);

        Assert.Equal(ReportStatuses.InReview, (await _api.GetReportAsync(id)).Status);
        Assert.Equal(3, _api.AuditedReads);
    }

    [Fact]
    public async Task The_queue_sorts_safety_first_then_the_oldest_and_defaults_to_pending()
    {
        var old = _api.AddReport(ReportReasons.Spam, hoursAgo: 40);
        var recent = _api.AddReport(ReportReasons.Harassment, hoursAgo: 2);
        var safety = _api.AddReport(ReportReasons.SafetyConcern, hoursAgo: 1);
        await _api.ResolveReportAsync(_api.AddReport(), new ResolveReportRequest(ModerationActions.Dismiss, null, null)); // decided: not pending

        var rows = (await _api.ListReportsAsync(new MessageReportQuery())).Items;

        Assert.Equal([safety, recent, old], rows.Select(r => r.Id));
        Assert.True(rows[0].IsSafety);
    }

    [Fact]
    public async Task Restricting_needs_one_seven_or_thirty_days_and_a_resolved_report_stays_resolved()
    {
        var id = _api.AddReport();

        Assert.Equal(400, (await Refused(() => _api.ResolveReportAsync(id, new ResolveReportRequest(ModerationActions.Restrict, 5, null)))).Status);
        Assert.Equal(400, (await Refused(() => _api.ResolveReportAsync(id, new ResolveReportRequest(ModerationActions.RemoveAndRestrict, null, null)))).Status);
        Assert.Equal(400, (await Refused(() => _api.ResolveReportAsync(id, new ResolveReportRequest(ModerationActions.Warn, null, new string('x', ModerationLimits.NoteToPeopleMaxLength + 1))))).Status);
        var done = await _api.ResolveReportAsync(id, new ResolveReportRequest(ModerationActions.Restrict, 7, "Be kind"));

        Assert.Equal(ReportStatuses.ActionTaken, done.Status);
        Assert.Single((await _api.ListRestrictionsAsync()).Items);
        Assert.Equal(409, (await Refused(() => _api.ResolveReportAsync(id, new ResolveReportRequest(ModerationActions.Dismiss, null, null)))).Status);
    }

    [Fact]
    public async Task Bulk_dismissal_takes_at_most_the_limit_and_skips_the_others()
    {
        var ids = Enumerable.Range(0, 3).Select(_ => _api.AddReport()).ToList();
        await _api.ResolveReportAsync(ids[2], new ResolveReportRequest(ModerationActions.Dismiss, null, null));

        await _api.BulkDismissAsync(new BulkDismissRequest([.. ids, Guid.NewGuid()], null)); // one already decided, one unknown: skipped, no error

        Assert.Equal(2, _api.Skipped.Count);
        Assert.Empty((await _api.ListReportsAsync(new MessageReportQuery())).Items);
        Assert.Equal(400, (await Refused(() => _api.BulkDismissAsync(new BulkDismissRequest([.. Enumerable.Range(0, ModerationLimits.BulkDismissMax + 1).Select(_ => Guid.NewGuid())], null)))).Status);
    }

    [Fact]
    public async Task An_attachment_is_served_for_the_reported_message_only_and_audited()
    {
        var id = _api.AddReport(withFile: true);

        var url = await _api.GetAttachmentAsync(id, _api.FileOf(id));

        Assert.Equal("homework.pdf", url.FileName);
        Assert.Equal(1, _api.AuditedReads);
        Assert.Equal(404, (await Refused(() => _api.GetAttachmentAsync(id, Guid.NewGuid()))).Status); // any other file
    }

    [Fact]
    public async Task A_restriction_is_lifted_at_once()
    {
        var id = _api.AddReport();
        await _api.ResolveReportAsync(id, new ResolveReportRequest(ModerationActions.Restrict, 1, null));
        var userId = (await _api.ListRestrictionsAsync()).Items[0].UserId;

        await _api.LiftRestrictionAsync(userId);

        Assert.Empty((await _api.ListRestrictionsAsync()).Items);
        Assert.Equal(404, (await Refused(() => _api.LiftRestrictionAsync(userId))).Status);
    }

    // ---- the page

    [Fact]
    public async Task The_page_needs_the_moderate_permission()
    {
        var page = await OpenAsync("en", Permissions.Messaging.Send);

        page.WaitForElement("[data-testid=forbidden-page]");
        Assert.Empty(page.FindAll("[data-testid=mod-queue], [data-testid=mod-title]"));
    }

    [Fact]
    public async Task The_queue_lists_safety_first_with_the_stats()
    {
        _api.AddReport(ReportReasons.Spam, hoursAgo: 30);
        var safety = _api.AddReport(ReportReasons.SafetyConcern, hoursAgo: 1);

        var page = await OpenAsync();

        Assert.Equal($"mod-row-{safety}", page.FindAll("[data-testid^=mod-row-]")[0].GetAttribute("data-testid"));
        Assert.Equal("2", page.Find("[data-testid=mod-stat-open]").TextContent.Trim());
    }

    [Fact]
    public async Task Opening_a_report_shows_the_frozen_context_and_nothing_to_read_on()
    {
        var id = _api.AddReport(reported: "You are useless");
        var page = await OpenAsync();

        await page.Find($"[data-testid=mod-open-{id}]").ClickAsync(new());

        page.WaitForElement("[data-testid=mod-context]");
        Assert.Equal(MessagingLimits.ReportContextEachSide * 2 + 1, page.FindAll("[data-testid=mod-context] [data-testid^=mod-msg-]").Count);
        Assert.Contains("You are useless", page.Find("[data-testid=mod-context]").TextContent);
        Assert.Contains("rest of the conversation is not available", page.Find("[data-testid=mod-frozen]").TextContent);
        Assert.Empty(page.FindAll("a[href*='messages/']")); // no link to the conversation
        Assert.Equal(1, _api.AuditedReads); // the read was audited
    }

    [Fact]
    public async Task The_page_never_names_the_reporter()
    {
        var id = _api.AddReport();
        var page = await OpenAsync();

        await page.Find($"[data-testid=mod-open-{id}]").ClickAsync(new());
        page.WaitForElement("[data-testid=mod-context]");

        Assert.DoesNotContain("Reporter:", page.Markup);
        Assert.Contains("Note from the reporter", page.Markup); // the note, not the person
    }

    [Fact]
    public async Task A_decision_says_what_it_does_and_restricting_sends_the_days_and_the_note()
    {
        var id = _api.AddReport();
        var page = await OpenAsync();
        await page.Find($"[data-testid=mod-open-{id}]").ClickAsync(new());
        page.WaitForElement("[data-testid=mod-decision]");
        Assert.True(page.Find("[data-testid=mod-apply]").HasAttribute("disabled")); // nothing chosen yet

        await page.InvokeAsync(() => page.FindComponent<ModerationDecision>().Instance.ValueChanged.InvokeAsync(ModerationAction.Restrict));
        await page.InvokeAsync(() => page.FindComponent<ModerationDecision>().Instance.DaysChanged.InvokeAsync(30));
        await page.InvokeAsync(() => page.FindComponents<MudTextField<string>>().First(f => f.Instance.Label == "Note for the sender (optional)").Instance.ValueChanged.InvokeAsync("Please be kind"));
        await page.Find("[data-testid=mod-apply]").ClickAsync(new());

        page.WaitForElement("[data-testid=mod-resolved]");
        Assert.Equal((ModerationActions.Restrict, 30, "Please be kind"), (_api.LastResolve!.Action, _api.LastResolve.DurationDays, _api.LastResolve.NoteToPeople));
        Assert.NotEmpty(page.FindAll("[data-testid^=mod-restriction-]"));
    }

    [Fact]
    public async Task Keep_selected_dismisses_in_bulk()
    {
        _api.AddReport();
        _api.AddReport();
        var page = await OpenAsync();

        await page.InvokeAsync(() => page.FindComponents<MudCheckBox<bool>>().First().Instance.ValueChanged.InvokeAsync(true));
        await page.Find("[data-testid=mod-keep-selected]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-testid^=mod-row-]")));
        Assert.Single((await _api.ListReportsAsync(new MessageReportQuery())).Items);
    }

    [Fact]
    public async Task A_restriction_can_be_lifted_from_the_page()
    {
        var id = _api.AddReport();
        await _api.ResolveReportAsync(id, new ResolveReportRequest(ModerationActions.Restrict, 7, null));
        var userId = (await _api.ListRestrictionsAsync()).Items[0].UserId;
        var page = await OpenAsync();

        await page.Find($"[data-testid=mod-lift-{userId}]").ClickAsync(new());

        page.WaitForElement("[data-testid=mod-no-restrictions]");
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        _api.AddReport();
        var page = await OpenAsync("ar");

        Assert.Equal("بلاغات الرسائل", page.Find("[data-testid=mod-title]").TextContent.Trim());
    }
}
