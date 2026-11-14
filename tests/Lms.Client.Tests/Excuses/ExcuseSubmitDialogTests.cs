using System.Globalization;
using Bunit;
using Lms.Client.Pages.Me;
using Lms.Client.Services.Api;
using Lms.Client.Services.Attendance;
using Lms.Client.Services.Excuses;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Lms.Shared.Excuses;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Excuses;

public sealed class ExcuseSubmitDialogTests : StaffTestContext
{
    private readonly FakeAttendanceApi _attendance = new();
    private readonly FakeExcusesApi _excuses = new();
    private readonly FailingExcuses _api;

    public ExcuseSubmitDialogTests()
    {
        _api = new FailingExcuses(_excuses);
        Services.AddSingleton<IAttendanceApi>(_attendance);
        Services.AddSingleton<IExcusesApi>(_api);
    }

    private async Task OpenDialogAsync()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Attendance.ViewOwn, Permissions.Excuses.Submit);
        RenderProviders();
        var page = Render<MyAttendancePage>();
        page.WaitForElement($"[data-testid=own-explain-{FakeAttendanceApi.AbsentSession}]").Click();
        Dialogs.WaitForElement("[data-testid=excuse-submit]");
    }

    [Fact]
    public async Task Policy_hint_uses_server_values()
    {
        await OpenDialogAsync();

        Assert.Contains("within 5 days", Dialogs.Find("[data-testid=excuse-policy-hint]").TextContent); // MaxDaysAfterAbsence from the policy
        Assert.Contains("5 MB", Dialogs.Markup);
        Assert.Contains("PDF", Dialogs.Markup); // the allowed types come from the policy too
    }

    [Fact]
    public async Task The_pressed_absence_is_ticked_and_its_day_fills_the_dates()
    {
        await OpenDialogAsync();

        Dialogs.WaitForElement("[data-testid=excuse-sessions]");
        var selected = Dialogs.FindComponent<Lms.Client.Pages.Excuses.ExcuseSessionsChecklist>().Instance.Selected;
        Assert.Equal([FakeAttendanceApi.AbsentSession], selected.ToList()); // the Late session is offered but not ticked
    }

    [Fact]
    public async Task Submit_needs_a_reason_and_then_sends_the_ticked_session()
    {
        await OpenDialogAsync();
        Dialogs.WaitForElement("[data-testid=excuse-sessions]");

        Dialogs.Find("[data-testid=excuse-submit]").Click();
        Assert.Empty(_excuses.All); // nothing is sent without a reason
        Assert.Contains("Choose a reason", Dialogs.Markup);

        var reason = Dialogs.FindComponents<MudSelect<string>>().Single();
        await ChooseAsync(reason, "Family");
        Dialogs.Find("[data-testid=excuse-submit]").Click();

        Dialogs.WaitForAssertion(() => Assert.Single(_excuses.All));
        Assert.Equal("Family", _excuses.All[0].Reason);
    }

    [Fact]
    public async Task Out_of_policy_codes_map_to_field_sentences()
    {
        _api.SubmitFailure = new ApiProblemException(409, "excuse_out_of_policy", null, null, reason: ExcusePolicyViolations.TooLate);
        await OpenDialogAsync();
        Dialogs.WaitForElement("[data-testid=excuse-sessions]");
        await ChooseAsync(Dialogs.FindComponents<MudSelect<string>>().Single(), "Family");

        Dialogs.Find("[data-testid=excuse-submit]").Click();

        // the sentence sits with the date fields, not only in a general alert
        Dialogs.WaitForAssertion(() => Assert.Contains("older than the period allowed", Dialogs.Markup));
        Assert.Contains("older than the period allowed", Dialogs.Find(".lms-form-grid").ParentElement!.TextContent); // next to the dates, not only in the alert
    }

    [Fact]
    public async Task Attachment_types_and_size_checked_before_upload()
    {
        await OpenDialogAsync();
        var input = Dialogs.FindComponent<InputFile>();

        input.UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "virus.exe", null, "application/x-msdownload"));
        Dialogs.WaitForAssertion(() => Assert.Contains("not allowed", Dialogs.Find("[data-testid=excuse-file-error]").TextContent));
        Assert.Empty(Dialogs.FindAll("[data-testid=excuse-file-name]"));

        input.UploadFiles(InputFileContent.CreateFromBinary(new byte[6 * 1024 * 1024], "huge.pdf", null, "application/pdf"));
        Dialogs.WaitForAssertion(() => Assert.Contains("larger than 5 MB", Dialogs.Find("[data-testid=excuse-file-error]").TextContent));

        input.UploadFiles(InputFileContent.CreateFromBinary([1, 2, 3], "note.pdf", null, "application/pdf"));
        Dialogs.WaitForAssertion(() => Assert.Equal("note.pdf", Dialogs.Find("[data-testid=excuse-file-name]").TextContent.Trim()));
        Assert.Empty(Dialogs.FindAll("[data-testid=excuse-file-error]"));
    }

    [Fact]
    public async Task Checklist_reloads_on_date_change()
    {
        await OpenDialogAsync();
        Dialogs.WaitForElement("[data-testid=excuse-sessions]");
        var from = Dialogs.FindComponents<MudDatePicker>().First();
        var other = new DateTime(2026, 11, 3);

        await Dialogs.InvokeAsync(() => from.Instance.DateChanged.InvokeAsync(other));

        Dialogs.WaitForAssertion(() => Assert.Contains("2026-11-03", Dialogs.Find("[data-testid=excuse-sessions]").TextContent));
        Assert.Single(Dialogs.FindComponent<Lms.Client.Pages.Excuses.ExcuseSessionsChecklist>().Instance.Selected); // a session still offered in the new range stays ticked
    }

    [Fact]
    public async Task Stacked_sheet_class_is_used_for_the_phone_layout()
    {
        await OpenDialogAsync();

        Assert.NotNull(Dialogs.Find(".lms-dialog-sheet")); // full screen under 600 px by CSS, the sections one under the other
        Assert.Equal(3, Dialogs.FindAll("fieldset.lms-fieldset").Count);
    }

    /// <summary>Lets a test make the server refuse a submission.</summary>
    private sealed class FailingExcuses(FakeExcusesApi inner) : IExcusesApi
    {
        public Exception? SubmitFailure { get; set; }
        public Task<ExcusePolicyDto> GetPolicyAsync(Guid studentId, CancellationToken ct = default) => inner.GetPolicyAsync(studentId, ct);
        public Task<IReadOnlyList<ExcusableSessionDto>> GetExcusableSessionsAsync(Guid studentId, DateOnly from, DateOnly to, CancellationToken ct = default) => inner.GetExcusableSessionsAsync(studentId, from, to, ct);
        public Task<ExcuseAttachmentDto> UploadAttachmentAsync(string fileName, string contentType, Stream content, CancellationToken ct = default) => inner.UploadAttachmentAsync(fileName, contentType, content, ct);
        public Task<ExcuseDto> SubmitAsync(SubmitExcuseRequest request, CancellationToken ct = default) => SubmitFailure is { } f ? Task.FromException<ExcuseDto>(f) : inner.SubmitAsync(request, ct);
        public Task<ExcuseDto> WithdrawAsync(Guid excuseId, CancellationToken ct = default) => inner.WithdrawAsync(excuseId, ct);
        public Task<PagedResult<ExcuseDto>> GetMineAsync(MyExcusesQuery query, CancellationToken ct = default) => inner.GetMineAsync(query, ct);
        public Task<PagedResult<ExcuseReviewItemDto>> GetReviewQueueAsync(ExcuseReviewQuery query, CancellationToken ct = default) => inner.GetReviewQueueAsync(query, ct);
        public Task<ExcuseDto> ApproveAsync(Guid excuseId, ApproveExcuseRequest request, CancellationToken ct = default) => inner.ApproveAsync(excuseId, request, ct);
        public Task<ExcuseDto> RejectAsync(Guid excuseId, RejectExcuseRequest request, CancellationToken ct = default) => inner.RejectAsync(excuseId, request, ct);
        public Task<ExcuseDto> EscalateAsync(Guid excuseId, EscalateExcuseRequest request, CancellationToken ct = default) => inner.EscalateAsync(excuseId, request, ct);
        public Task<IReadOnlyList<EscalationTargetDto>> GetEscalationTargetsAsync(Guid excuseId, CancellationToken ct = default) => inner.GetEscalationTargetsAsync(excuseId, ct);
        public Task<ExcuseAttachmentUrlDto> GetAttachmentUrlAsync(Guid excuseId, CancellationToken ct = default) => inner.GetAttachmentUrlAsync(excuseId, ct);
    }
}
