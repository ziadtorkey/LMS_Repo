using Lms.Infrastructure.Packages;

namespace Lms.UnitTests.Packages;

/// <summary>Content types come from the extension allow-list, never from the archive (M4b Task 8a).</summary>
public sealed class PackageContentTypesTests
{
    [Theory]
    [InlineData("index.html", "text/html; charset=utf-8", true)]
    [InlineData("a/b/PAGE.HTM", "text/html; charset=utf-8", true)]
    [InlineData("js/app.js", "text/javascript; charset=utf-8", true)]
    [InlineData("img/logo.svg", "image/svg+xml", true)]
    [InlineData("media/video.MP4", "video/mp4", true)]
    [InlineData("fonts/a.woff2", "font/woff2", true)]
    [InlineData("data.json", "application/json", true)]
    [InlineData("imsmanifest.xml", "application/xml", true)]
    public void Listed_extensions_get_their_type(string path, string type, bool inline) =>
        Assert.Equal(new PackageContentType(type, inline), PackageContentTypes.For(path));

    [Theory]
    [InlineData("notes.unknown")]
    [InlineData("noextension")]
    [InlineData("archive.exe")]
    [InlineData("config.php")]
    [InlineData("x.html.bak")]
    public void Anything_else_is_an_octet_stream_download(string path) =>
        Assert.Equal(new PackageContentType("application/octet-stream", false), PackageContentTypes.For(path));
}
