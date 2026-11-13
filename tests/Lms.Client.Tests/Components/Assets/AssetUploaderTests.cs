using Bunit;
using Lms.Client.Components.Assets;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Components.Assets;

public sealed class AssetUploaderTests : ClientTestContext
{
    private readonly FakeAssetsApi _api = new();
    private readonly Guid _classCourse = Guid.NewGuid();
    private readonly List<AssetDto> _uploaded = [];

    public AssetUploaderTests() => Services.AddSingleton<IAssetsApi>(_api);

    private IRenderedComponent<AssetUploader> RenderUploader(IReadOnlyList<string>? kinds = null)
    {
        Render<MudPopoverProvider>();
        var cut = Render<AssetUploader>(p => p
            .Add(x => x.ClassCourseId, _classCourse)
            .Add(x => x.AcceptedKinds, kinds)
            .Add(x => x.OnUploaded, EventCallback.Factory.Create<AssetDto>(this, a => _uploaded.Add(a))));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=asset-limits]")));
        return cut;
    }

    private static void Upload(IRenderedComponent<AssetUploader> cut, string name, int bytes = 3, string contentType = "application/octet-stream") =>
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(new byte[bytes], name, contentType: contentType));

    // UploadFiles returns only when the change handler has finished, so a gated upload must start on another thread.
    private static void UploadWithoutWaiting(IRenderedComponent<AssetUploader> cut, string name, string contentType) =>
        _ = Task.Run(() => Upload(cut, name, contentType: contentType));

    [Fact]
    public void Shows_the_limits_up_front()
    {
        _api.MaxBytes = 3 * 1024 * 1024;
        var cut = RenderUploader([AssetKinds.Video]);

        var text = cut.Find("[data-testid=asset-limits]").TextContent;
        Assert.Contains(".mp4", text);
        Assert.DoesNotContain(".pdf", text);
        Assert.Contains("3", text);
    }

    [Theory]
    [InlineData("script.exe")]
    [InlineData("vector.svg")]
    [InlineData("noextension")]
    public void Rejects_disallowed_type_before_upload(string name)
    {
        var cut = RenderUploader();

        Upload(cut, name);

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=asset-error]")));
        Assert.Empty(_api.Uploads);
    }

    [Fact]
    public void Rejects_a_kind_the_module_does_not_accept()
    {
        var cut = RenderUploader([AssetKinds.Audio]);

        Upload(cut, "clip.mp4"); // a video is allowed in general, not for an Audio module

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=asset-error]")));
        Assert.Empty(_api.Uploads);
    }

    [Fact]
    public void Rejects_disallowed_type_and_oversize_before_upload()
    {
        _api.MaxBytes = 10;
        var cut = RenderUploader();

        Upload(cut, "big.pdf", bytes: 11);

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=asset-error]")));
        Assert.Empty(_api.Uploads);
    }

    [Fact]
    public void Shows_progress_and_completes()
    {
        var gate = _api.Gate = new TaskCompletionSource();
        var cut = RenderUploader();

        UploadWithoutWaiting(cut, "plan.pdf", "application/pdf");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=asset-progress]")));
        gate.SetResult();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=asset-status]")));
        Assert.Empty(cut.FindAll("[data-testid=asset-progress]"));
        var upload = Assert.Single(_api.Uploads);
        Assert.Equal((_classCourse, "plan.pdf", "application/pdf", 3L), upload);
        Assert.Equal("plan.pdf", Assert.Single(_uploaded).FileName);
    }

    [Fact]
    public async Task Cancel_stops_the_upload()
    {
        _api.Gate = new TaskCompletionSource();
        var cut = RenderUploader();
        UploadWithoutWaiting(cut, "plan.pdf", "application/pdf");
        // The upload is in flight when it has reached the gate. Its 50 % progress report re-renders the component, and a button found
        // before that render has a stale event handler id (UnknownEventHandlerIdException), so wait until the render has caught up too.
        await _api.GateReached.Task.WaitAsync(TimeSpan.FromSeconds(10));   // throws TimeoutException if the upload never reached the gate
        cut.WaitForAssertion(() => Assert.Equal("50", cut.Find("[data-testid=asset-progress]").GetAttribute("aria-valuenow")));

        cut.Find("[data-testid=asset-cancel]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=asset-progress]")));
        Assert.Empty(_api.Uploads);
        Assert.Empty(_uploaded);
    }

    [Fact]
    public void A_failed_upload_can_be_retried()
    {
        _api.FailuresLeft = 1;
        _api.Failure = new ApiProblemException(400, "asset_too_large", null, null);
        var cut = RenderUploader();

        Upload(cut, "plan.pdf");
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=asset-retry]")));
        _api.Failure = null;
        cut.Find("[data-testid=asset-retry]").Click();

        cut.WaitForAssertion(() => Assert.Single(_uploaded));
        Assert.Single(_api.Uploads);
    }

    [Fact]
    public void The_extension_list_is_isolated_left_to_right_in_arabic()
    {
        Culture.Current = new System.Globalization.CultureInfo("ar");
        var cut = RenderUploader([AssetKinds.Video]);

        var bdi = cut.Find("[data-testid=asset-limits] bdi");

        Assert.Equal("ltr", bdi.GetAttribute("dir"));
        Assert.Contains(".mp4", bdi.TextContent);
        Assert.Contains("ميجابايت", cut.Find("[data-testid=asset-limits]").TextContent);
    }
}
