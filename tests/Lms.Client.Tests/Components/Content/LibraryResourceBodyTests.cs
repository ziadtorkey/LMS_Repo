using System.Globalization;
using Bunit;
using Lms.Client.Components.Content.Kinds;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Content;
using Lms.Shared.Library;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Components.Content;

public sealed class LibraryResourceBodyTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly FakeLibraryApi _library = new();
    private readonly FakeAssetsApi _assets = new();

    public LibraryResourceBodyTests()
    {
        Services.AddSingleton<ILibraryApi>(_library);
        Services.AddSingleton<IAssetsApi>(_assets);
        JSInterop.Setup<string>("lmsBlobUrl", _ => true).SetResult("blob:reader-test");
        Culture.Current = new CultureInfo("en");
    }

    private static AssetDto Pdf() =>
        new(Guid.NewGuid(), Guid.NewGuid(), AssetKinds.Document, "syllabus.pdf", "application/pdf", 10, null, true, DateTime.UtcNow);

    private IRenderedComponent<LibraryResourceBody> Open(Guid? id)
    {
        Render<MudPopoverProvider>();
        var cut = Render<LibraryResourceBody>(p => p.Add(x => x.ResourceId, id));
        cut.WaitForElement("[data-testid=library-body], [data-testid=library-body-empty], [data-testid=library-body-error]", Wait);
        return cut;
    }

    [Fact]
    public void A_file_resource_shows_the_description_and_the_preview_with_a_download_button()
    {
        var r = _library.Add("Syllabus", file: Pdf(), downloadAllowed: true) with { DescriptionEn = "The term syllabus" };
        _library.Resources[0] = r;

        var cut = Open(r.Id);

        Assert.Contains("The term syllabus", cut.Find("[data-testid=library-body]").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid=asset-preview]"));
        Assert.NotEmpty(cut.FindAll("[data-testid=library-download]"));
    }

    [Fact]
    public void A_reader_only_resource_is_read_through_the_reader_route_not_the_asset_url()
    {
        var file = Pdf();
        var r = _library.Add("Reader only", file: file, downloadAllowed: false);
        _assets.Refuse(file.Id);   // the server's /api/assets/{id}/url answers 4xx for a file of a reader-only resource

        var cut = Open(r.Id);
        cut.WaitForElement("[data-testid=library-reader-pdf]", Wait);

        Assert.Equal("blob:reader-test", cut.Find("[data-testid=library-reader-pdf]").GetAttribute("src"));
        Assert.Equal([r.Id], _library.Reads);
        Assert.Empty(_assets.UrlRequests);
        Assert.Empty(cut.FindAll("[data-testid=asset-preview-failed]"));
        Assert.Empty(cut.FindAll("[data-testid=library-download]"));
        Assert.Empty(cut.FindAll("[data-testid=asset-download]"));
    }

    [Fact]
    public void A_downloadable_resource_still_previews_through_the_asset_url()
    {
        var file = Pdf();
        var r = _library.Add("Open", file: file, downloadAllowed: true);

        var cut = Open(r.Id);

        Assert.NotEmpty(cut.FindAll("[data-testid=asset-preview]"));
        cut.WaitForAssertion(() => Assert.Equal([file.Id], _assets.UrlRequests));
        Assert.Empty(_library.Reads);
    }

    [Fact]
    public void A_reader_that_fails_shows_the_error()
    {
        var r = _library.Add("Reader only", file: Pdf(), downloadAllowed: false);
        _library.Failure = new ApiProblemException(404, "not_found", null, null);

        var cut = Open(r.Id);

        cut.WaitForElement("[data-testid=library-body-error]", Wait);
    }

    [Fact]
    public void Download_asks_the_library_for_a_signed_url_and_navigates_to_it()
    {
        var r = _library.Add("Syllabus", file: Pdf(), downloadAllowed: true);
        var cut = Open(r.Id);

        cut.Find("[data-testid=library-download]").Click();

        cut.WaitForAssertion(() => Assert.Equal($"https://files.test/library/{r.Id}", Services.GetRequiredService<NavigationManager>().Uri));
    }

    [Fact]
    public void A_refused_download_shows_the_error_and_stays_on_the_page()
    {
        var r = _library.Add("Syllabus", file: Pdf(), downloadAllowed: true);
        var cut = Open(r.Id);
        // The resource switched to reader-only after the page was opened: the server answers 403 download_not_allowed.
        _library.Resources[0] = _library.Resources[0] with { DownloadAllowed = false };

        cut.Find("[data-testid=library-download]").Click();

        var error = cut.WaitForElement("[data-testid=library-body-error]", Wait);
        Assert.Contains("Download is disabled", error.TextContent);   // the server's code download_disabled, with its own message
        Assert.DoesNotContain("files.test", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void A_link_resource_opens_the_address_in_a_new_tab()
    {
        var r = _library.Add("Khan", LibraryResourceTypes.Link, url: "https://example.test/khan");

        var cut = Open(r.Id);

        var link = cut.Find("[data-testid=library-link]");
        Assert.Equal("https://example.test/khan", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Contains("noopener", link.GetAttribute("rel"));
        Assert.Empty(cut.FindAll("[data-testid=library-download]"));
    }

    [Fact]
    public void A_link_that_is_not_http_is_not_offered()
    {
        var r = _library.Add("Bad", LibraryResourceTypes.Link, url: "javascript:alert(1)");

        var cut = Open(r.Id);

        Assert.Empty(cut.FindAll("[data-testid=library-link]"));
        Assert.NotEmpty(cut.FindAll("[data-testid=library-link-blocked]"));
    }

    [Fact]
    public void A_missing_reference_shows_the_empty_state()
    {
        var cut = Open(null);

        Assert.NotEmpty(cut.FindAll("[data-testid=library-body-empty]"));
    }

    [Fact]
    public void A_resource_the_server_cannot_find_shows_an_error_not_a_crash()
    {
        var cut = Open(Guid.NewGuid());

        Assert.NotEmpty(cut.FindAll("[data-testid=library-body-error]"));
    }

    [Fact]
    public void The_title_follows_the_language()
    {
        var r = _library.Add("Syllabus", file: Pdf());
        Culture.Current = new CultureInfo("ar");

        var cut = Open(r.Id);

        Assert.Contains("Syllabus ar", cut.Find("[data-testid=library-body]").TextContent);
    }
}
