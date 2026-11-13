using Lms.Domain.Packages;
using Lms.Infrastructure.Packages;
using Lms.Infrastructure.Packages.Scorm;
using Lms.IntegrationTests.Packages.Corpus;

namespace Lms.IntegrationTests.Packages.Scorm;

/// <summary>The SCORM validator on the crafted corpus (M4b Task 10): each archive is hardened and extracted like the validation job does, then validated.</summary>
public sealed class ScormManifestValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lms-scorm-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<PackageValidationResult> ValidateAsync(string corpus)
    {
        var c = CorpusCases.All.Single(x => x.Name == corpus);
        await using var archive = new FileStream(CorpusCases.CommittedFile(c), FileMode.Open, FileAccess.Read, FileShare.Read);
        var inspection = await ZipHardening.InspectAsync(archive, new ZipHardeningLimits());
        Assert.True(inspection.Accepted, $"{corpus}: {inspection.RejectReason}");
        using var area = new PackageTempArea(_root, 64L * 1024 * 1024);
        await ZipHardening.ExtractAsync(archive, inspection, (entry, content) => area.WriteAsync(entry.Name, content, default));
        return await new ScormManifestValidator().ValidateAsync(area, default);
    }

    [Theory]
    [InlineData(4, 100, true)]
    [InlineData(5, 110, false)]
    public async Task A_launch_path_longer_than_the_column_is_an_invalid_manifest(int folders, int length, bool valid)
    {
        var path = string.Join('/', Enumerable.Range(0, folders).Select(i => new string((char)('a' + i), length))) + "/start.html";
        var manifest = $"<?xml version=\"1.0\"?><manifest xmlns=\"http://www.imsproject.org/xsd/imscp_rootv1p1p2\" xmlns:adlcp=\"http://www.adlnet.org/xsd/adlcp_rootv1p2\">"
                       + "<organizations default=\"o\"><organization identifier=\"o\"><title>T</title><item identifier=\"i\" identifierref=\"r\"><title>T</title></item></organization></organizations>"
                       + $"<resources><resource identifier=\"r\" type=\"webcontent\" adlcp:scormtype=\"sco\" href=\"{path}\"><file href=\"{path}\"/></resource></resources></manifest>";
        var zip = RawZipWriter.Write([RawZipWriter.File("imsmanifest.xml", manifest), RawZipWriter.File(path, "<html><body>x</body></html>")]);
        await using var archive = new MemoryStream(zip);
        var inspection = await ZipHardening.InspectAsync(archive, new ZipHardeningLimits());
        Assert.True(inspection.Accepted, inspection.RejectReason);
        using var area = new PackageTempArea(_root, 64L * 1024 * 1024);
        await ZipHardening.ExtractAsync(archive, inspection, (entry, content) => area.WriteAsync(entry.Name, content, default));

        var result = await new ScormManifestValidator().ValidateAsync(area, default);

        Assert.Equal(valid, result.Valid);
        Assert.Equal(valid ? null : "manifest_invalid", result.RejectReason);
        Assert.True(path.Length > Lms.Domain.Packages.PackageUpload.MaxLaunchPathLength == !valid);
    }

    [Fact]
    public async Task Scorm12_and_2004_are_detected()
    {
        var v12 = await ValidateAsync("scorm12_valid");
        var v2004 = await ValidateAsync("scorm2004_valid");

        Assert.Equal(("Scorm12", "index.html", "SCO"), (v12.Version, v12.LaunchPath, v12.Title));
        Assert.Equal(("Scorm2004", "index.html"), (v2004.Version, v2004.LaunchPath));
        Assert.True(v12.Valid && v2004.Valid);
    }

    [Theory]
    [InlineData("multi_sco", "multi_sco")]
    [InlineData("scorm2004_sequencing", "multi_sco")]
    [InlineData("xxe_file_entity", "xml_unsafe")]
    [InlineData("xxe_http_entity", "xml_unsafe")]
    [InlineData("xml_billion_laughs", "xml_unsafe")]
    [InlineData("doctype_without_entities", "xml_unsafe")]
    [InlineData("manifest_huge", "xml_unsafe")]
    [InlineData("manifest_not_xml", "manifest_invalid")]
    [InlineData("no_manifest", "manifest_invalid")]
    [InlineData("manifest_launch_file_missing", "manifest_invalid")]
    [InlineData("manifest_href_parent_escape", "manifest_invalid")]
    [InlineData("manifest_href_absolute_url", "manifest_invalid")]
    [InlineData("manifest_href_file_scheme", "manifest_invalid")]
    [InlineData("manifest_href_javascript_scheme", "manifest_invalid")]
    [InlineData("manifest_href_backslash", "manifest_invalid")]
    [InlineData("manifest_href_protocol_relative", "manifest_invalid")]
    [InlineData("xml_base_hides_missing_launch_file", "manifest_invalid")]
    [InlineData("launch_frameset", "needs_nested_frames")]
    [InlineData("launch_iframe_to_local_page", "needs_nested_frames")]
    public async Task Hostile_or_unsupported_packages_are_rejected_with_the_corpus_reason(string corpus, string reason)
    {
        var result = await ValidateAsync(corpus);

        Assert.Equal(reason, result.RejectReason);
        Assert.Equal(CorpusCases.All.Single(c => c.Name == corpus).Expect, reason);
    }

    [Theory]
    [InlineData("manifest_schema_location_and_xml_base_never_fetched")]
    [InlineData("manifest_utf8_bom")]
    [InlineData("scorm_multipage_sco")]
    [InlineData("html_utf8_bom")]
    [InlineData("html_utf16le_bom")]
    [InlineData("html_no_head")]
    [InlineData("sco_defines_its_own_window_api")]
    [InlineData("sco_catches_the_parent_walk_error")]
    [InlineData("sco_meta_refresh_outside")]
    public async Task In_scope_packages_validate(string corpus)
    {
        var result = await ValidateAsync(corpus);

        Assert.True(result.Valid, $"{corpus} was rejected as {result.RejectReason}");
        Assert.Equal("Scorm12", result.Version);
        Assert.Equal("index.html", result.LaunchPath);
    }

    [Fact]
    public async Task The_launch_file_is_found_against_the_package_root_and_xml_base_is_ignored()
    {
        var withBase = await ValidateAsync("manifest_schema_location_and_xml_base_never_fetched");
        var hidden = await ValidateAsync("xml_base_hides_missing_launch_file");

        Assert.Equal("index.html", withBase.LaunchPath);
        Assert.Equal("manifest_invalid", hidden.RejectReason);
    }

    [Theory]
    [InlineData("index.html", "index.html")]
    [InlineData("a/b/page.html?x=1#frag", "a/b/page.html")]
    [InlineData("caf%C3%A9.html", "café.html")]
    [InlineData("../x.html", null)]
    [InlineData("a/../x.html", null)]
    [InlineData("/x.html", null)]
    [InlineData("//host/x.html", null)]
    [InlineData("http://evil/x.html", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("a\\b.html", null)]
    [InlineData("", null)]
    [InlineData("a//b.html", null)]
    public void Launch_hrefs_resolve_only_inside_the_package(string href, string? expected) =>
        Assert.Equal(expected, ScormLaunchResolver.Normalize(href));

    [Fact]
    public void Frames_are_found_in_markup_but_not_in_comments()
    {
        static byte[] B(string s) => System.Text.Encoding.UTF8.GetBytes(s);

        Assert.True(ScormLaunchResolver.UsesFrames(B("<html><FRAMESET rows=\"*\"><frame src=a.html></frameset></html>")));
        Assert.True(ScormLaunchResolver.UsesFrames(B("<body><iframe src=\"x.html\"></iframe></body>")));
        Assert.False(ScormLaunchResolver.UsesFrames(B("<body><!-- <iframe src=\"x.html\"> --><p>plain</p></body>")));
        Assert.False(ScormLaunchResolver.UsesFrames(B("<body><p>the word iframe is text</p></body>")));
    }
}
