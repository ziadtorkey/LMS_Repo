using System.Text;

namespace Lms.IntegrationTests.Packages.Corpus;

/// <summary>What a package of the corpus must do when it reaches the package kernel.</summary>
public enum CorpusKind
{
    /// <summary>Refused by the hardening reader / validation job with <see cref="CorpusCase.Expect"/> as the reject reason.</summary>
    Reject,

    /// <summary>A well-formed package that must pass validation (and, for <c>Accept</c>, tell the reader the quirk is not an attack).</summary>
    Accept,

    /// <summary>Passes validation; the hostile behaviour is in the content and is exercised in a browser (sandbox probes, prelude injection).</summary>
    Runtime,
}

/// <summary>
/// One crafted package. <see cref="Expect"/> is <c>"accepted"</c> or a <c>PackageRejectReasons</c> value (the Task 1 catalog);
/// <see cref="Has"/> proves on the raw bytes that the hostile feature is really in the file, so a corpus file cannot silently
/// become harmless (a writer that "fixes" the name, a library that drops the symlink bit).
/// </summary>
public sealed record CorpusCase(
    string Name,
    CorpusKind Kind,
    string Expect,
    string Intent,
    Func<byte[]> Build,
    Func<RawZipView, bool> Has,
    bool Heavy = false,
    string? Notes = null,
    bool Raw = false)
{
    public const string Accepted = "accepted";

    public string FileName => Name + ".zip";
}

/// <summary>Reject reasons of the wave 3b catalog (<c>PackageRejectReasons</c>); copied here so the corpus compiles before that contract is on main (a test pins the two lists together once it is).</summary>
public static class CorpusReasons
{
    public const string UnsafePath = "unsafe_path", TooManyEntries = "too_many_entries", TooLarge = "too_large", CompressionRatio = "compression_ratio",
        Symlink = "symlink", DuplicateName = "duplicate_name", BadNameEncoding = "bad_name_encoding", Executable = "executable",
        Encrypted = "encrypted", NestedArchive = "nested_archive", ManifestInvalid = "manifest_invalid", XmlUnsafe = "xml_unsafe",
        MultiSco = "multi_sco", NeedsNestedFrames = "needs_nested_frames", H5pInvalid = "h5p_invalid",
        InvalidArchive = "invalid_archive", BadName = "bad_name";

    public static readonly string[] All =
    [
        UnsafePath, TooManyEntries, TooLarge, CompressionRatio, Symlink, DuplicateName, BadNameEncoding, Executable, Encrypted,
        NestedArchive, ManifestInvalid, XmlUnsafe, MultiSco, NeedsNestedFrames, "needs_relaxed_policy", H5pInvalid, "validation_timeout",
        "quota_exceeded", "scan_failed", InvalidArchive, BadName,
    ];
}

/// <summary>Small builders shared by the case files: a valid SCORM 1.2 / 2004 / H5P base that each hostile case extends by one defect.</summary>
internal static class Pkg
{
    public static readonly string Sco = """
        <!doctype html><html><head><meta charset="utf-8"><title>SCO</title></head><body>
        <script>
        function findAPI(w){var n=0;while(w.API==null&&w.parent!=null&&w.parent!=w&&n++<10){w=w.parent}return w.API}
        var api=findAPI(window);if(api){api.LMSInitialize("");api.LMSSetValue("cmi.core.lesson_status","completed");api.LMSCommit("");api.LMSFinish("")}
        </script><p>Hello</p></body></html>
        """.ReplaceLineEndings("\n");

    public static string Manifest12(string href = "index.html", string extraResources = "", string attrs = "", string extraItems = "") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <manifest identifier="m1" version="1.0" xmlns="http://www.imsproject.org/xsd/imscp_rootv1p1p2" xmlns:adlcp="http://www.adlnet.org/xsd/adlcp_rootv1p2" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"{attrs}>
          <organizations default="o1"><organization identifier="o1"><title>Corpus</title>
            <item identifier="i1" identifierref="r1"><title>SCO</title></item>{extraItems}</organization></organizations>
          <resources><resource identifier="r1" type="webcontent" adlcp:scormtype="sco" href="{href}"><file href="{href}"/></resource>{extraResources}</resources>
        </manifest>
        """.ReplaceLineEndings("\n");

    public static string Manifest2004(string href = "index.html", string extraAttrs = "") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <manifest identifier="m1" version="1.0" xmlns="http://www.imsglobal.org/xsd/imscp_v1p1" xmlns:adlcp="http://www.adlnet.org/xsd/adlcp_v1p3" xmlns:adlseq="http://www.adlnet.org/xsd/adlseq_v1p3" xmlns:imsss="http://www.imsglobal.org/xsd/imsss"{extraAttrs}>
          <metadata><schema>ADL SCORM</schema><schemaversion>2004 4th Edition</schemaversion></metadata>
          <organizations default="o1"><organization identifier="o1"><title>Corpus</title>
            <item identifier="i1" identifierref="r1"><title>SCO</title></item></organization></organizations>
          <resources><resource identifier="r1" type="webcontent" adlcp:scormType="sco" href="{href}"><file href="{href}"/></resource></resources>
        </manifest>
        """.ReplaceLineEndings("\n");

    public static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    public static RawEntry[] Scorm12(string? launch = null, string? manifest = null) =>
    [
        RawZipWriter.File("imsmanifest.xml", manifest ?? Manifest12()),
        RawZipWriter.File("index.html", launch ?? Sco),
    ];

    public static byte[] Pack(IEnumerable<RawEntry> entries, RawZipOptions? options = null) => RawZipWriter.Write(entries, options);

    public static byte[] Scorm12With(params RawEntry[] extra) => Pack(Scorm12().Concat(extra));

    /// <summary>A small, valid, stored archive (the payload of a nested-archive case).</summary>
    public static byte[] InnerZip() => Pack([RawZipWriter.File("inner.txt", "hello")]);

    public static bool AnyName(RawZipView v, Func<string, bool> test) => v.Records.Any(r => test(r.NameUtf8));

    public static bool AnyBytes(RawZipView v, Func<byte[], bool> test) => v.Records.Any(r => test(r.NameBytes));

    public static string Json(int depth) => string.Concat(Enumerable.Repeat("{\"a\":", depth)) + "1" + new string('}', depth);
}
