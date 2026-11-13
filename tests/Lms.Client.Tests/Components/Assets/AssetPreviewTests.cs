using Bunit;
using Lms.Client.Components.Assets;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Components.Assets;

public sealed class AssetPreviewTests : ClientTestContext
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly FakeAssetsApi _api = new();
    private readonly ManualTime _time = new();

    public AssetPreviewTests()
    {
        _api.UrlLifetime = TimeSpan.FromMinutes(5);
        Services.AddSingleton<IAssetsApi>(_api);
        Services.AddSingleton<TimeProvider>(_time);
    }

    private static AssetDto Make(string kind, string name, bool previewable = true) =>
        new(Guid.NewGuid(), Guid.NewGuid(), kind, name, "x/y", 10, null, previewable, DateTime.UtcNow);

    private async Task<IRenderedComponent<AssetPreview>> RenderPreviewAsync(AssetDto asset, params string[] permissions)
    {
        await GrantAsync(permissions);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        Render<MudPopoverProvider>();
        return Render<AssetPreview>(p => p.Add(x => x.Asset, asset));
    }

    [Theory]
    [InlineData(AssetKinds.Image, "a.png", "asset-image")]
    [InlineData(AssetKinds.Audio, "a.mp3", "asset-audio")]
    [InlineData(AssetKinds.Video, "a.mp4", "asset-video")]
    [InlineData(AssetKinds.Document, "a.pdf", "asset-pdf")]
    public async Task Shows_the_native_viewer_for_each_previewable_kind(string kind, string name, string testId)
    {
        var cut = await RenderPreviewAsync(Make(kind, name));

        cut.WaitForAssertion(() => Assert.StartsWith("https://files.test/", cut.Find($"[data-testid={testId}]").GetAttribute("src")));
    }

    [Fact]
    public async Task Expired_signed_url_is_refreshed_once()
    {
        var cut = await RenderPreviewAsync(Make(AssetKinds.Video, "a.mp4"));
        var first = cut.WaitForElement("[data-testid=asset-video]").GetAttribute("src");

        _time.Now += TimeSpan.FromMinutes(6); // the URL lived 5 minutes on the server clock
        cut.Find("[data-testid=asset-video]").TriggerEvent("onerror", new Microsoft.AspNetCore.Components.Web.ErrorEventArgs());

        cut.WaitForAssertion(() => Assert.NotEqual(first, cut.Find("[data-testid=asset-video]").GetAttribute("src")));
        Assert.Equal(2, _api.UrlRequests.Count);

        // A second failure does not loop: the reload button appears instead.
        _time.Now += TimeSpan.FromMinutes(6);
        cut.Find("[data-testid=asset-video]").TriggerEvent("onerror", new Microsoft.AspNetCore.Components.Web.ErrorEventArgs());
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=asset-preview-failed]")));
        Assert.Equal(2, _api.UrlRequests.Count);
    }

    [Fact]
    public async Task A_failure_before_expiry_is_not_a_refresh_case()
    {
        var cut = await RenderPreviewAsync(Make(AssetKinds.Image, "a.png"));
        cut.WaitForElement("[data-testid=asset-image]");

        _time.Now += TimeSpan.FromMinutes(1);
        cut.Find("[data-testid=asset-image]").TriggerEvent("onerror", new Microsoft.AspNetCore.Components.Web.ErrorEventArgs());

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=asset-preview-failed]")));
        Assert.Single(_api.UrlRequests);
    }

    [Fact]
    public async Task Download_hidden_without_permission()
    {
        var cut = await RenderPreviewAsync(Make(AssetKinds.Document, "a.docx", previewable: false));

        Assert.NotNull(cut.Find("[data-testid=asset-no-preview]"));
        Assert.Empty(cut.FindAll("[data-testid=asset-download]"));
        Assert.Empty(_api.UrlRequests); // no URL is fetched when nothing will use it
    }

    [Fact]
    public async Task Download_offered_with_Files_Download()
    {
        var cut = await RenderPreviewAsync(Make(AssetKinds.Document, "a.docx", previewable: false), Permissions.Files.Download);

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=asset-download]")));
    }
}
