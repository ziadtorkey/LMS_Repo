using System.Globalization;
using Bunit;
using Lms.Client.Components.Packages;
using Lms.Client.Services.Api;
using Lms.Client.Services.Packages;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.Packages;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Packages;

public sealed class PackageUploadDialogTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly FakePackagesApi _packages = new();
    private readonly FakeAssetsApi _assets = new();
    private readonly PackagePollOptions _poll = new() { Interval = TimeSpan.FromMilliseconds(5), MaxInterval = TimeSpan.FromMilliseconds(10), MaxPolls = 50 };
    private readonly Guid _classCourse = Guid.NewGuid();
    private readonly Guid _lesson = Guid.NewGuid();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public PackageUploadDialogTests()
    {
        Services.AddSingleton<IPackagesApi>(_packages);
        Services.AddSingleton<IAssetsApi>(_assets);
        Services.AddSingleton(_poll);
        Culture.Current = new CultureInfo("en");
    }

    private IReadOnlyList<PackageLessonOption> Lessons => [new(_lesson, "الدرس الأول", "Lesson one")];

    private Task<ModuleNodeDto?>? _result;

    private void Open(string kind = PackageKinds.Scorm, Guid? resume = null, bool rtl = false)
    {
        Render<MudPopoverProvider>();
        _dialogs = rtl
            ? Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<MudDialogProvider>()).FindComponent<MudDialogProvider>()
            : Render<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        _dialogs.InvokeAsync(() => _result = PackageUploadDialog.ShowAsync(dialogs, "Add a package", _classCourse, kind, Lessons, _lesson, resume));
        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=package-dialog]")), Wait);
    }

    private void Upload(string name, int bytes = 3) =>
        _dialogs.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(new byte[bytes], name));

    [Fact]
    public void Rejects_wrong_extension_and_oversize_before_upload()
    {
        _assets.MaxBytes = 4;
        Open();

        Upload("notes.pdf");
        _dialogs.WaitForAssertion(() => Assert.Contains(".zip", _dialogs.Find("[data-testid=package-error]").TextContent), Wait);
        Upload("big.zip", bytes: 10);
        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=package-error]")), Wait);

        Assert.Empty(_packages.Uploads);
    }

    [Fact]
    public void H5p_asks_for_an_h5p_file()
    {
        Open(PackageKinds.H5p);

        Upload("lesson.zip");

        _dialogs.WaitForAssertion(() => Assert.Contains(".h5p", _dialogs.Find("[data-testid=package-error]").TextContent), Wait);
        Assert.Empty(_packages.Uploads);
    }

    [Fact]
    public void Shows_progress_then_validating_then_ready()
    {
        _packages.ChecksBeforeReady = 3;
        Open();

        Upload("lesson.zip");

        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=package-attach-form]")), Wait);
        Assert.Equal("lesson", _dialogs.Find("[data-testid=package-found-title]").TextContent);
        Assert.Equal([(_classCourse, PackageKinds.Scorm, _lesson, "lesson.zip")], _packages.Uploads);
        Assert.True(_packages.StatusReads >= 3);
    }

    [Fact]
    public void Polling_stops_when_the_dialog_closes()
    {
        // The wait between two polls is a gate the test controls: no sleeping, no timing.
        var gate = new SemaphoreSlim(0);
        var waits = new List<CancellationToken>();
        _poll.Delay = async (_, ct) =>
        {
            lock (waits) waits.Add(ct);
            await gate.WaitAsync(ct);
        };
        _packages.ChecksBeforeReady = 100_000;
        Open();
        Upload("slow.zip");
        _dialogs.WaitForAssertion(() => Assert.Single(waits), Wait);
        var readsWhileOpen = _packages.StatusReads;

        _dialogs.Find("[data-testid=package-close]").Click();

        _dialogs.WaitForAssertion(() => Assert.Empty(_dialogs.FindAll("[data-testid=package-dialog]")), Wait);
        _dialogs.WaitForAssertion(() => Assert.True(waits[0].IsCancellationRequested), Wait);
        gate.Release(10);
        Assert.Equal(readsWhileOpen, _packages.StatusReads);
        Assert.Single(waits);
    }

    [Fact]
    public void Polling_is_capped_and_offers_check_again()
    {
        _poll.MaxPolls = 3;
        _packages.ChecksBeforeReady = 100_000;
        Open();
        Upload("slow.zip");

        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=package-check-again]")), Wait);
        Assert.Equal(3, _packages.StatusReads);

        _packages.ChecksBeforeReady = 0;
        _dialogs.Find("[data-testid=package-check-again]").Click();

        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=package-attach-form]")), Wait);
    }

    [Fact]
    public void Nested_frames_package_shows_the_plain_reason()
    {
        Open();

        Upload("needs_nested_frames.zip");

        _dialogs.WaitForAssertion(() => Assert.Contains("frames", _dialogs.Find("[data-testid=package-reject-reason]").TextContent), Wait);
        Assert.Empty(_dialogs.FindAll("[data-testid=package-attach-form]"));
    }

    [Fact]
    public void Choosing_another_file_deletes_the_rejected_upload_and_returns_to_step_one()
    {
        Open();
        Upload("encrypted.zip");
        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=package-reject]")), Wait);

        _dialogs.Find("[data-testid=package-choose-another]").Click();

        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=package-input]")), Wait);
        Assert.Single(_packages.Deleted);
    }

    [Fact]
    public async Task Ready_package_can_be_attached_with_a_completion_rule()
    {
        Open();
        Upload("Fractions.zip");
        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=package-attach-form]")), Wait);

        var rule = _dialogs.FindComponents<MudSelect<string>>().Single(s => s.Markup.Contains("package-rule"));
        await _dialogs.InvokeAsync(() => rule.Instance.ValueChanged.InvokeAsync(CompletionRules.Score));
        _dialogs.WaitForAssertion(() => Assert.False(_dialogs.Find("[data-testid=package-attach]").HasAttribute("disabled")), Wait);
        _dialogs.Find("[data-testid=package-attach]").Click();

        _dialogs.WaitForAssertion(() => Assert.Single(_packages.Attached), Wait);
        var attached = _packages.Attached[0].Module;
        Assert.Equal(CompletionRules.Score, attached.CompletionRule);
        Assert.Equal("Fractions", attached.TitleEn);
        Assert.Equal("Fractions", attached.TitleAr);
        _dialogs.WaitForAssertion(() => Assert.True(_result!.IsCompleted), Wait);
        Assert.Equal(ModuleKinds.Scorm, (await _result!)!.Kind);
    }

    [Fact]
    public void Attach_is_disabled_until_ready()
    {
        var package = Dto(PackageUploadStatuses.Validating);

        var cut = Render<PackageAttachForm>(p => p
            .Add(x => x.Package, package).Add(x => x.Lessons, Lessons)
            .Add(x => x.OnAttach, _ => Task.FromResult<string?>(null)));

        Assert.True(cut.Find("[data-testid=package-attach]").HasAttribute("disabled"));
    }

    [Fact]
    public void Feature_disabled_shows_the_plain_message()
    {
        _packages.FeatureDisabled = true;
        Open();

        Upload("lesson.zip");

        _dialogs.WaitForAssertion(() => Assert.Contains("turned off", _dialogs.Find("[data-testid=package-blocked]").TextContent), Wait);
        Assert.Empty(_dialogs.FindAll("[data-testid=package-input]"));
    }

    [Fact]
    public void Package_host_not_configured_shows_the_plain_message()
    {
        _packages.HostNotConfigured = true;
        Open();

        Upload("lesson.zip");

        _dialogs.WaitForAssertion(() => Assert.Contains("platform administrator", _dialogs.Find("[data-testid=package-blocked]").TextContent), Wait);
    }

    [Theory]
    [InlineData(403, "feature_disabled", "turned off")]
    [InlineData(409, "package_host_not_configured", "platform administrator")]
    [InlineData(409, "package_too_large", "storage is full")]
    public void Blocking_states_replace_the_drop_zone_as_soon_as_the_dialog_opens(int status, string code, string text)
    {
        _packages.ListFailure = new ApiProblemException(status, code, null, null);

        Open();

        _dialogs.WaitForAssertion(() => Assert.Contains(text, _dialogs.Find("[data-testid=package-blocked]").TextContent), Wait);
        Assert.Empty(_dialogs.FindAll("[data-testid=package-input]"));
        Assert.Empty(_dialogs.FindAll("[data-testid=package-dropzone]"));
        Assert.Empty(_packages.Uploads);
    }

    [Fact]
    public void An_unrelated_failure_of_the_open_time_check_keeps_the_drop_zone()
    {
        _packages.ListFailure = new ApiProblemException(500, "server_error", null, null);

        Open();

        _dialogs.WaitForAssertion(() => Assert.Equal(1, _packages.ListReads), Wait);
        Assert.NotNull(_dialogs.Find("[data-testid=package-input]"));
        Assert.Empty(_dialogs.FindAll("[data-testid=package-blocked]"));
    }

    [Fact]
    public void A_status_read_that_fails_shows_the_reason_in_the_check_step()
    {
        // The package does not exist for the fake: the status read answers 404.
        Open(resume: Guid.NewGuid());

        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.Find("[data-testid=package-poll-error]").TextContent), Wait);
        Assert.NotNull(_dialogs.Find("[data-testid=package-check-again]"));
        Assert.Equal("step", _dialogs.Find("[data-testid=package-step-2]").GetAttribute("aria-current"));
    }

    [Fact]
    public async Task Reopening_a_package_that_is_already_ready_goes_straight_to_the_attach_step()
    {
        var uploaded = await _packages.UploadAsync(_classCourse, PackageKinds.Scorm, _lesson, "done.zip", new MemoryStream([1]));
        _packages.ChecksBeforeReady = 0;

        Open(resume: uploaded.Id);

        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=package-attach-form]")), Wait);
        Assert.Equal("step", _dialogs.Find("[data-testid=package-step-3]").GetAttribute("aria-current"));
        Assert.Empty(_dialogs.FindAll("[data-testid=package-input]"));
    }

    [Fact]
    public async Task Reopening_a_rejected_package_shows_the_reason_and_offers_another_file()
    {
        var uploaded = await _packages.UploadAsync(_classCourse, PackageKinds.Scorm, _lesson, "encrypted.zip", new MemoryStream([1]));

        Open(resume: uploaded.Id);

        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=package-reject-reason]")), Wait);
        Assert.Contains("lms-step--failed", _dialogs.Find("[data-testid=package-step-2]").ClassName);
        Assert.NotNull(_dialogs.Find("[data-testid=package-choose-another]"));
        Assert.Empty(_dialogs.FindAll("[data-testid=package-attach-form]"));
    }

    [Fact]
    public void The_lesson_is_shown_not_chosen()
    {
        Open();
        Upload("lesson.zip");
        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=package-attach-form]")), Wait);

        Assert.Contains("Lesson one", _dialogs.Find("[data-testid=package-lesson]").TextContent);
        Assert.Empty(_dialogs.FindComponents<MudSelect<Guid>>());
    }

    [Fact]
    public void A_failing_attach_shows_the_reason_and_the_button_works_again()
    {
        Open();
        Upload("lesson.zip");
        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=package-attach-form]")), Wait);
        _packages.AttachFailure = new ApiProblemException(409, ProblemCodes.package_not_ready, null, null);

        _dialogs.Find("[data-testid=package-attach]").Click();

        _dialogs.WaitForAssertion(() => Assert.Contains("not ready", _dialogs.Find("[data-testid=package-attach-error]").TextContent), Wait);
        Assert.False(_dialogs.Find("[data-testid=package-attach]").HasAttribute("disabled"));
        Assert.Empty(_packages.Attached);

        _packages.AttachFailure = null;
        _dialogs.Find("[data-testid=package-attach]").Click();

        _dialogs.WaitForAssertion(() => Assert.Single(_packages.Attached), Wait);
        _dialogs.WaitForAssertion(() => Assert.True(_result!.IsCompleted), Wait);
    }

    [Fact]
    public void Delete_asks_for_confirmation_and_removes_the_package()
    {
        Open();
        Upload("lesson.zip");
        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=package-delete]")), Wait);

        _dialogs.Find("[data-testid=package-delete]").Click();
        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=confirm-yes]")), Wait);
        _dialogs.Find("[data-testid=confirm-yes]").Click();

        _dialogs.WaitForAssertion(() => Assert.Single(_packages.Deleted), Wait);
        _dialogs.WaitForAssertion(() => Assert.Empty(_dialogs.FindAll("[data-testid=package-dialog]")), Wait);
    }

    [Fact]
    public void Cancelling_the_delete_confirmation_keeps_the_package_and_the_dialog()
    {
        Open();
        Upload("lesson.zip");
        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=package-delete]")), Wait);

        _dialogs.Find("[data-testid=package-delete]").Click();
        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=confirm-no]")), Wait);
        _dialogs.Find("[data-testid=confirm-no]").Click();

        _dialogs.WaitForAssertion(() => Assert.Empty(_dialogs.FindAll("[data-testid=confirm-no]")), Wait);
        Assert.Empty(_packages.Deleted);
        Assert.NotNull(_dialogs.Find("[data-testid=package-attach-form]"));
    }

    [Fact]
    public async Task A_package_that_is_still_being_checked_can_be_reopened_at_the_check_step()
    {
        var uploaded = await _packages.UploadAsync(_classCourse, PackageKinds.Scorm, _lesson, "resume.zip", new MemoryStream([1]));
        _packages.ChecksBeforeReady = 100_000;

        Open(resume: uploaded.Id);

        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=package-checking]")), Wait);
        Assert.Equal("step", _dialogs.Find("[data-testid=package-step-2]").GetAttribute("aria-current"));
    }

    [Fact]
    public void Page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        Open(rtl: true);

        Assert.Contains("رفع", _dialogs.Find("[data-testid=package-step-1]").TextContent);
        Assert.Contains("ميغابايت", _dialogs.Find("[data-testid=package-limits]").TextContent);

        Upload("Fractions.zip");
        _dialogs.WaitForAssertion(() => Assert.NotNull(_dialogs.Find("[data-testid=package-attach-form]")), Wait);
        // Text fields keep the direction of their language whatever the page direction; the file name is always left to right.
        Assert.Equal("rtl", _dialogs.Find("[data-testid=package-title-ar]").GetAttribute("dir"));
        Assert.Equal("ltr", _dialogs.Find("[data-testid=package-title-en]").GetAttribute("dir"));
        Assert.Equal("ltr", _dialogs.Find("[data-testid=package-summary] bdi").GetAttribute("dir"));
        Assert.Contains("الدرس الأول", _dialogs.Find("[data-testid=package-lesson]").TextContent);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public void Every_reject_reason_has_its_own_plain_text_in_both_languages(string culture)
    {
        Culture.Current = new CultureInfo(culture);
        var fallback = Render<PackageRejectReason>(p => p.Add(x => x.Reason, "no_such_reason")).Find("[data-testid=package-reject-reason]").TextContent;

        foreach (var reason in PackageRejectReasons.All)
        {
            var text = Render<PackageRejectReason>(p => p.Add(x => x.Reason, reason)).Find("[data-testid=package-reject-reason]").TextContent;
            Assert.NotEqual(fallback, text);
            Assert.False(string.IsNullOrWhiteSpace(text), reason);
        }
    }

    private PackageUploadDto Dto(string status) =>
        new(Guid.NewGuid(), _classCourse, _lesson, "lesson.zip", PackageKinds.Scorm, status, null, "Lesson", "2004", null, null, DateTime.UtcNow);
}
