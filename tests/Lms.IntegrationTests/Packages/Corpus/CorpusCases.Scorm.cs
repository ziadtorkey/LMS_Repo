using System.Text;
using static Lms.IntegrationTests.Packages.Corpus.CorpusReasons;
using static Lms.IntegrationTests.Packages.Corpus.Pkg;

namespace Lms.IntegrationTests.Packages.Corpus;

/// <summary>Manifest and launch-page cases: XML attacks, hrefs that leave the package, multi-SCO, framesets, and the valid base packages.</summary>
public static partial class CorpusCases
{
    private static bool ManifestContains(RawZipView v, string text) =>
        v.Records.Any(r => r.NameUtf8 == "imsmanifest.xml" && Encoding.UTF8.GetString(v.ReadData(r)).Contains(text, StringComparison.Ordinal));

    private static CorpusCase ManifestCase(string name, string reason, string manifest, string intent, string mustContain, string? notes = null) =>
        Reject(name, reason, intent, () => Pack(Scorm12(manifest: manifest)), v => ManifestContains(v, mustContain), notes: notes);

    private static IEnumerable<CorpusCase> Manifests()
    {
        yield return Accept("scorm12_valid", "A valid single-SCO SCORM 1.2 package.", () => Pack(Scorm12()), v => v.Records.Count == 2);
        yield return Accept("scorm2004_valid", "A valid single-SCO SCORM 2004 4th edition package.", () => Pack([RawZipWriter.File("imsmanifest.xml", Manifest2004()), RawZipWriter.File("index.html", Sco.Replace("LMSInitialize", "Initialize"))]), v => ManifestContains(v, "2004 4th Edition"));

        yield return ManifestCase("xxe_file_entity", XmlUnsafe, "<?xml version=\"1.0\"?><!DOCTYPE m [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>" + Manifest12(attrs: "").Replace("<title>Corpus</title>", "<title>&x;</title>").Split('\n', 2)[1], "A DOCTYPE with an external file entity used in the title (DTD processing is prohibited).", "file:///etc/passwd");
        yield return ManifestCase("xxe_http_entity", XmlUnsafe, "<?xml version=\"1.0\"?><!DOCTYPE m [<!ENTITY x SYSTEM \"http://127.0.0.1:9/xxe\">]>" + Manifest12().Replace("<title>Corpus</title>", "<title>&x;</title>").Split('\n', 2)[1], "A DOCTYPE with an external http entity: the validator must never open a connection.", "127.0.0.1:9/xxe", "The integration test runs a loopback listener on port 9 and asserts it never gets a request.");
        yield return ManifestCase("xml_billion_laughs", XmlUnsafe, "<?xml version=\"1.0\"?><!DOCTYPE lolz [<!ENTITY l0 \"lol\"><!ENTITY l1 \"&l0;&l0;&l0;&l0;&l0;&l0;&l0;&l0;&l0;&l0;\"><!ENTITY l2 \"&l1;&l1;&l1;&l1;&l1;&l1;&l1;&l1;&l1;&l1;\"><!ENTITY l3 \"&l2;&l2;&l2;&l2;&l2;&l2;&l2;&l2;&l2;&l2;\"><!ENTITY l4 \"&l3;&l3;&l3;&l3;&l3;&l3;&l3;&l3;&l3;&l3;\"><!ENTITY l5 \"&l4;&l4;&l4;&l4;&l4;&l4;&l4;&l4;&l4;&l4;\"><!ENTITY l6 \"&l5;&l5;&l5;&l5;&l5;&l5;&l5;&l5;&l5;&l5;\"><!ENTITY l7 \"&l6;&l6;&l6;&l6;&l6;&l6;&l6;&l6;&l6;&l6;\"><!ENTITY l8 \"&l7;&l7;&l7;&l7;&l7;&l7;&l7;&l7;&l7;&l7;\"><!ENTITY l9 \"&l8;&l8;&l8;&l8;&l8;&l8;&l8;&l8;&l8;&l8;\">]>" + Manifest12().Replace("<title>Corpus</title>", "<title>&l9;</title>").Split('\n', 2)[1], "Exponential entity expansion (a billion laughs): ~1 GB of text from a 1 KB file.", "&l9;");
        yield return ManifestCase("doctype_without_entities", XmlUnsafe, "<?xml version=\"1.0\"?><!DOCTYPE manifest>" + Manifest12().Split('\n', 2)[1], "A bare DOCTYPE: DTD processing is prohibited outright, not only entity expansion.", "<!DOCTYPE manifest>");
        yield return ManifestCase("manifest_not_xml", ManifestInvalid, "this is { not xml", "A manifest that is not XML.", "not xml");
        yield return ManifestCase("manifest_huge", XmlUnsafe, Manifest12().Replace("<resources>", "<!--" + new string('a', 3 * 1024 * 1024) + "--><resources>"), "A 3 MB manifest (document size cap).", "<!--aaaa");

        yield return Reject("manifest_launch_file_missing", ManifestInvalid, "The launch href names a file that is not in the package.", () => Pack([RawZipWriter.File("imsmanifest.xml", Manifest12("missing.html")), RawZipWriter.File("index.html", Sco)]), v => ManifestContains(v, "missing.html"));
        yield return ManifestHref("manifest_href_parent_escape", "../outside.html", "A launch href that climbs out of the package (and could select another package's file).");
        yield return ManifestHref("manifest_href_absolute_url", "http://evil.example/launch.html", "A launch href that is an absolute http URL.");
        yield return ManifestHref("manifest_href_file_scheme", "file:///etc/passwd", "A launch href with the file: scheme.");
        yield return ManifestHref("manifest_href_javascript_scheme", "javascript:alert(1)", "A launch href with the javascript: scheme.");
        yield return ManifestHref("manifest_href_backslash", "sub\\index.html", "A launch href with a backslash.");
        yield return ManifestHref("manifest_href_protocol_relative", "//evil.example/launch.html", "A protocol-relative launch href.");

        yield return Reject(
            "multi_sco",
            MultiSco,
            "A manifest with two SCOs and two items (multi-SCO courses are out of v1).",
            () => Pack(Scorm12(manifest: Manifest12(extraResources: "<resource identifier=\"r2\" type=\"webcontent\" adlcp:scormtype=\"sco\" href=\"two.html\"><file href=\"two.html\"/></resource>", extraItems: "<item identifier=\"i2\" identifierref=\"r2\"><title>Two</title></item>")).Append(RawZipWriter.File("two.html", Sco))),
            v => ManifestContains(v, "two.html"));

        yield return Reject(
            "scorm2004_sequencing",
            MultiSco,
            "SCORM 2004 simple sequencing rules (imsss:sequencing) that the v1 runtime does not implement.",
            () => Pack([RawZipWriter.File("imsmanifest.xml", Manifest2004().Replace("<title>SCO</title></item>", "<title>SCO</title><imsss:sequencing><imsss:controlMode choice=\"false\" flow=\"true\"/></imsss:sequencing></item>")), RawZipWriter.File("index.html", Sco)]),
            v => ManifestContains(v, "imsss:sequencing"));

        yield return Accept(
            "manifest_schema_location_and_xml_base_never_fetched",
            "xsi:schemaLocation and xml:base pointing at a loopback URL: the validator must not fetch anything and must resolve the href against the package root.",
            () => Pack([RawZipWriter.File("imsmanifest.xml", Manifest12(attrs: " xsi:schemaLocation=\"http://www.imsproject.org/xsd/imscp_rootv1p1p2 http://127.0.0.1:9/imscp.xsd\" xml:base=\"http://127.0.0.1:9/base/\"")), RawZipWriter.File("index.html", Sco)]),
            v => ManifestContains(v, "127.0.0.1:9"),
            "The integration test runs a loopback listener on port 9 and asserts it never gets a request.");

        yield return Reject(
            "xml_base_hides_missing_launch_file",
            ManifestInvalid,
            "The launch file exists only if xml:base is honoured (it is not in the package): the href is resolved against the package root, so the file is missing.",
            () => Pack([RawZipWriter.File("imsmanifest.xml", Manifest12(href: "launch.html", attrs: " xml:base=\"content/\"")), RawZipWriter.File("content/launch.html", Sco)]),
            v => ManifestContains(v, "xml:base=\"content/\""));

        yield return Accept("manifest_utf8_bom", "A manifest that starts with a UTF-8 byte order mark.", () => Pack([new RawEntry { Name = "imsmanifest.xml", Data = [.. Encoding.UTF8.GetPreamble(), .. Utf8(Manifest12())] }, RawZipWriter.File("index.html", Sco)]), v => v.ReadData(v.Find("imsmanifest.xml"))[0] == 0xEF);

        yield return Reject("launch_frameset", NeedsNestedFrames, "A launch page that is a frameset (the SCO sits in a child frame that looks in parent for the API).", () => Pack(Scorm12(launch: "<html><frameset rows=\"0,*\"><frame src=\"api.html\"><frame src=\"content.html\"></frameset></html>").Append(RawZipWriter.File("content.html", Sco)).Append(RawZipWriter.File("api.html", "<html></html>"))), v => AnyName(v, n => n == "content.html"));
        yield return Reject("launch_iframe_to_local_page", NeedsNestedFrames, "A launch page whose only content is an iframe of another page of the package.", () => Pack(Scorm12(launch: "<html><body><iframe src=\"content.html\" width=\"100%\" height=\"600\"></iframe></body></html>").Append(RawZipWriter.File("content.html", Sco))), v => AnyName(v, n => n == "content.html"));
    }

    private static CorpusCase ManifestHref(string name, string href, string intent) =>
        Reject(name, ManifestInvalid, intent, () => Pack([RawZipWriter.File("imsmanifest.xml", Manifest12(href.Replace("&", "&amp;"))), RawZipWriter.File("index.html", Sco)]), v => ManifestContains(v, href));
}
