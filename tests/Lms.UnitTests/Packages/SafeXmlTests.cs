using System.Net;
using System.Net.Sockets;
using System.Text;
using Lms.Infrastructure.Packages;

namespace Lms.UnitTests.Packages;

/// <summary>SCORM manifests and other package XML are parsed with no resolver at all (M4b Task 8b, #427 7.1).</summary>
public sealed class SafeXmlTests
{
    private static MemoryStream Xml(string text) => new(Encoding.UTF8.GetBytes(text));

    private const string Plain = "<manifest identifier=\"m\"><title>Corpus</title></manifest>";

    [Fact]
    public void A_well_formed_document_parses()
    {
        var result = SafeXml.Parse(Xml(Plain));

        Assert.Null(result.RejectReason);
        Assert.Equal("Corpus", result.Document!.Root!.Element("title")!.Value);
    }

    [Fact]
    public void Xxe_and_billion_laughs_are_rejected_without_network()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var http = $"<?xml version=\"1.0\"?><!DOCTYPE m [<!ENTITY x SYSTEM \"http://127.0.0.1:{port}/xxe\">]><m>&x;</m>";
        var file = "<?xml version=\"1.0\"?><!DOCTYPE m [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><m>&x;</m>";
        var laughs = "<?xml version=\"1.0\"?><!DOCTYPE l [<!ENTITY a \"lol\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\"><!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\">]><l>&c;</l>";
        var bare = "<?xml version=\"1.0\"?><!DOCTYPE manifest><manifest/>";

        foreach (var xml in new[] { http, file, laughs, bare })
        {
            var result = SafeXml.Parse(Xml(xml));

            Assert.Null(result.Document);
            Assert.Equal("xml_unsafe", result.RejectReason);
        }

        Assert.False(listener.Pending(), "the parser opened a connection");
    }

    [Fact]
    public void Schema_location_and_xml_base_are_never_fetched()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var xml = $"<m xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:schemaLocation=\"urn:x http://127.0.0.1:{port}/a.xsd\" "
                  + $"xml:base=\"http://127.0.0.1:{port}/base/\"><include xmlns=\"http://www.w3.org/2001/XInclude\" href=\"http://127.0.0.1:{port}/i.xml\"/></m>";

        var result = SafeXml.Parse(Xml(xml));

        Assert.Null(result.RejectReason);
        Assert.NotNull(result.Document);
        Assert.False(listener.Pending(), "the parser opened a connection");
    }

    [Fact]
    public void A_document_over_the_cap_is_rejected_as_unsafe()
    {
        var result = SafeXml.Parse(Xml("<m><!--" + new string('a', 2000) + "--></m>"), maxCharacters: 1000);

        Assert.Equal("xml_unsafe", result.RejectReason);
    }

    [Theory]
    [InlineData("this is { not xml")]
    [InlineData("<m><unclosed></m>")]
    [InlineData("")]
    public void Anything_not_well_formed_is_an_invalid_manifest(string text)
    {
        var result = SafeXml.Parse(Xml(text));

        Assert.Equal("manifest_invalid", result.RejectReason);
    }

    [Fact]
    public void A_utf8_byte_order_mark_is_accepted()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Plain)).ToArray();

        Assert.Null(SafeXml.Parse(new MemoryStream(bytes)).RejectReason);
    }
}
