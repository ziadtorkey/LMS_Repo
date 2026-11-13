using System.Text;
using static Lms.IntegrationTests.Packages.Corpus.CorpusReasons;
using static Lms.IntegrationTests.Packages.Corpus.Pkg;

namespace Lms.IntegrationTests.Packages.Corpus;

/// <summary>Archive-level attacks: path escapes, bombs, symlinks, duplicates, names, flags, Zip64 and structure. Each file is a valid SCORM 1.2 package plus exactly one defect.</summary>
public static partial class CorpusCases
{
    private static CorpusCase Reject(string name, string reason, string intent, Func<byte[]> build, Func<RawZipView, bool> has, bool heavy = false, string? notes = null, bool raw = false) =>
        new(name, CorpusKind.Reject, reason, intent, build, has, heavy, notes, raw);

    private static CorpusCase Accept(string name, string intent, Func<byte[]> build, Func<RawZipView, bool> has, string? notes = null, CorpusKind kind = CorpusKind.Accept) =>
        new(name, kind, CorpusCase.Accepted, intent, build, has, false, notes);

    private static CorpusCase PathCase(string name, string reason, string entryName, string intent) =>
        Reject(name, reason, intent, () => Scorm12With(RawZipWriter.File(entryName, "x")), v => AnyName(v, n => n == entryName));

    private static IEnumerable<CorpusCase> Paths()
    {
        yield return PathCase("zip_slip_dotdot", UnsafePath, "../evil.js", "Zip-slip: an entry that climbs out of the extraction root.");
        yield return PathCase("zip_slip_nested_dotdot", UnsafePath, "a/b/../../../evil.js", "Zip-slip that only escapes after normalising the middle segments.");
        yield return PathCase("zip_slip_backslash", UnsafePath, "..\\evil.js", "Zip-slip with a Windows separator: a reader that converts \\ to / extracts outside the root.");
        yield return PathCase("backslash_inside_name", UnsafePath, "sub\\page.html", "A backslash anywhere in a name is refused, not converted (the rules of #427 7.1).");
        yield return PathCase("absolute_unix", UnsafePath, "/etc/cron.d/evil", "Absolute path.");
        yield return PathCase("absolute_drive_letter", UnsafePath, "C:/Windows/evil.js", "Drive-letter absolute path.");
        yield return PathCase("drive_relative", UnsafePath, "C:evil.js", "Drive-relative path (no slash after the colon).");
        yield return PathCase("unc_path", UnsafePath, "//server/share/evil.js", "UNC path.");
        yield return PathCase("trailing_dot_name", BadName, "index.html.", "Windows strips a trailing dot, so this name becomes index.html and can shadow it.");
        yield return PathCase("trailing_space_name", BadName, "index.html ", "Windows strips a trailing space, same shadowing as the trailing dot.");
        yield return PathCase("name_too_long", BadName, new string('a', 300) + ".js", "A 304-character name (cap 255).");
        yield return PathCase("path_too_deep", BadName, string.Join('/', Enumerable.Range(0, 25).Select(i => "d" + i)) + "/f.js", "25 nested directories (cap 20).");

        foreach (var device in new[] { "CON", "nul.txt", "Aux.html", "COM1.js", "lpt9" })
        {
            yield return PathCase("device_name_" + device.ToLowerInvariant().Replace('.', '_'), BadName, device, "Windows device name: opening it hangs or writes to a device.");
        }

        yield return Reject(
            "local_name_differs_from_central",
            BadName,
            "The central directory says index2.html, the local header says ../evil.js: a reader that streams local headers extracts outside the root, one that reads the directory sees a safe name.",
            () => Pack(Scorm12().Append(new RawEntry { Name = "index2.html", LocalNameBytes = Utf8("../evil.js"), Data = Utf8("x") })),
            v => AnyName(v, n => n == "index2.html"),
            notes: "The reader must compare both names (or only trust one consistently) and refuse a mismatch.");

        yield return Accept(
            "percent_encoded_dotdot_is_a_literal_name",
            "A name that contains %2e%2e%2f is an ordinary file name: it must be stored literally and never URL-decoded into ../.",
            () => Scorm12With(RawZipWriter.File("%2e%2e%2fnot-a-parent.js", "x")),
            v => AnyName(v, n => n.StartsWith("%2e%2e", StringComparison.Ordinal)),
            "Also guards the package host: the stored key must stay literal when the request path is decoded once.");
    }

    private static IEnumerable<CorpusCase> Sizes()
    {
        var mb = new byte[1024 * 1024];
        yield return Reject(
            "bomb_ratio_one_entry",
            CompressionRatio,
            "One entry of 64 MB zeros deflates to about 64 KB (ratio ~1000:1; cap 100:1 per entry).",
            () => Pack(Scorm12().Append(new RawEntry { Name = "big.bin", Data = mb, Repeat = 64, Deflate = true })),
            v => v.Find("big.bin").UncompressedSize > 50L * 1024 * 1024,
            heavy: true,
            notes: "Needs a test cap of at most 100:1 per entry and the 64 MB entry under the total cap, so only the ratio rule can fire.");

        yield return Reject(
            "bomb_total_many_entries",
            TooLarge,
            "Twenty entries of 8 MB zeros: each is under the ratio-limited cap on its own, the total (160 MB) exceeds a total cap of 100 MB.",
            () => Pack(Scorm12().Concat(Enumerable.Range(0, 20).Select(i => new RawEntry { Name = $"part{i}.bin", Data = mb, Repeat = 8, Deflate = true }))),
            v => v.Records.Sum(r => r.UncompressedSize) > 100L * 1024 * 1024,
            heavy: true,
            notes: "Needs a test total cap of at most 100 MB; the ratio is also over, so the reader must report too_large first (total before ratio) or the test must set the ratio cap above 1000:1.");

        yield return Reject(
            "bomb_lying_header",
            TooLarge,
            "The headers say 100 bytes, the deflate stream is 50 MB: counting header sizes passes it, counting real bytes does not.",
            () => Pack(Scorm12().Append(new RawEntry { Name = "lie.bin", Data = mb, Repeat = 50, Deflate = true, DeclaredUncompressedSize = 100 })),
            v => v.Find("lie.bin").UncompressedSize == 100 && v.ReadData(v.Find("lie.bin"), 4 * 1024 * 1024).Length > 1024 * 1024,
            heavy: true,
            notes: "Needs a test total cap of at most 40 MB. 'Never trust headers': real bytes decide.");

        yield return Reject(
            "too_many_entries",
            TooManyEntries,
            "12 000 empty files (cap 10 000).",
            () => Pack(Scorm12().Concat(Enumerable.Range(0, 12_000).Select(i => RawZipWriter.File($"f/{i}.txt", [])))),
            v => v.Records.Count > 10_000);

        yield return Reject(
            "zip64_declared_huge",
            TooLarge,
            "A Zip64 entry whose extra field claims 6 GB while the real data is 5 bytes: 64-bit counters must be read, and a size over the cap refused.",
            () => Pack(Scorm12().Append(new RawEntry { Name = "huge.bin", Data = Utf8("hello"), Zip64 = true, DeclaredUncompressedSize = 6L * 1024 * 1024 * 1024 })),
            v => v.Find("huge.bin").UncompressedSize > 4L * 1024 * 1024 * 1024,
            notes: "A reader that rejects header/real mismatches may report too_large as well; the intended reason is the cap check on the 64-bit value.");

        yield return Accept(
            "zip64_valid_small",
            "A valid package written with Zip64 extra fields and a Zip64 end record: the reader must use 64-bit counters and still accept it.",
            () => Pack(Scorm12().Select(e => new RawEntry { Name = e.Name, Data = e.Data, Zip64 = true }), new RawZipOptions { Zip64EndRecord = true }),
            v => v.HasZip64EndRecord && v.Records.All(r => r.UncompressedSize > 0 && r.UncompressedSize < 100_000));

        yield return Accept(
            "data_descriptor_streamed",
            "A valid package whose entries use a data descriptor (sizes 0 in the local header): the reader must not trust local-header sizes.",
            () => Pack(Scorm12().Select(e => new RawEntry { Name = e.Name, Data = e.Data, DataDescriptor = true })),
            v => v.Records.All(r => r.DataDescriptor));
    }

    private static IEnumerable<CorpusCase> Entries()
    {
        yield return Reject(
            "symlink_entry",
            Symlink,
            "A Unix symlink (mode 0120777) whose content is the target path.",
            () => Scorm12With(new RawEntry { Name = "link.html", Data = Utf8("../../../etc/passwd"), ExternalAttributes = RawEntry.UnixSymlink, VersionMadeBy = RawEntry.UnixMadeBy }),
            v => v.Find("link.html").IsSymlink,
            notes: "ZIP has no hardlink entry type; hardlinks only exist in tar, which is rejected as a nested archive.");

        yield return Reject(
            "encrypted_entry",
            Encrypted,
            "General-purpose flag bit 0 (traditional PKWARE encryption) on an entry.",
            () => Scorm12With(new RawEntry { Name = "secret.bin", Data = Utf8("not really encrypted"), Encrypted = true }),
            v => v.Find("secret.bin").Encrypted);

        foreach (var ext in new[] { "exe", "dll", "so", "php", "jsp", "asp", "aspx", "cgi", "sh", "bat", "cmd", "ps1" })
        {
            yield return Reject(
                "executable_" + ext,
                Executable,
                $"A .{ext} file (executable or server-side script).",
                () => Scorm12With(RawZipWriter.File("tools/run." + ext, "x")),
                v => AnyName(v, n => n.EndsWith("." + ext, StringComparison.Ordinal)));
        }

        yield return Reject(
            "executable_upper_case_extension",
            Executable,
            "RUN.ExE: the extension check is case-insensitive.",
            () => Scorm12With(RawZipWriter.File("RUN.ExE", "x")),
            v => AnyName(v, n => n == "RUN.ExE"));

        foreach (var ext in new[] { "zip", "jar", "war", "7z", "rar", "tar", "gz", "tgz" })
        {
            yield return Reject(
                "nested_archive_" + ext,
                NestedArchive,
                $"A nested .{ext} archive (never extracted; v1 rejects it).",
                () => Scorm12With(RawZipWriter.File("media/inner." + ext, ext == "zip" ? InnerZip() : Utf8("PK\u0003\u0004 not a real " + ext))),
                v => AnyName(v, n => n.EndsWith("." + ext, StringComparison.Ordinal)));
        }

        yield return Reject(
            "duplicate_exact",
            DuplicateName,
            "The same name twice: which one wins depends on the reader (a later one can overwrite the validated one).",
            () => Pack(Scorm12().Append(RawZipWriter.File("index.html", "second"))),
            v => v.Records.Count(r => r.NameUtf8 == "index.html") == 2);

        yield return Reject(
            "duplicate_case_fold",
            DuplicateName,
            "index.html and INDEX.HTML collide on a case-insensitive store.",
            () => Pack(Scorm12().Append(RawZipWriter.File("INDEX.HTML", "second"))),
            v => AnyName(v, n => n == "INDEX.HTML") && AnyName(v, n => n == "index.html"));

        yield return Reject(
            "duplicate_nfc_nfd",
            DuplicateName,
            "café.html written once in NFC (é = U+00E9) and once in NFD (e + U+0301): different bytes, same name after normalisation.",
            () => Scorm12With(RawZipWriter.File("caf\u00e9.html", "a"), RawZipWriter.File("cafe\u0301.html", "b")),
            v => AnyName(v, n => n == "caf\u00e9.html") && AnyName(v, n => n == "cafe\u0301.html"));

        yield return Reject(
            "duplicate_in_directory_case",
            DuplicateName,
            "dir/A.js and DIR/a.js: the collision is in a directory segment, not the file name.",
            () => Scorm12With(RawZipWriter.File("dir/A.js", "a"), RawZipWriter.File("DIR/a.js", "b")),
            v => AnyName(v, n => n == "dir/A.js") && AnyName(v, n => n == "DIR/a.js"));
    }

    private static IEnumerable<CorpusCase> Names()
    {
        yield return Reject("name_bidi_override", BadNameEncoding, "U+202E (right-to-left override) in a name: exe.js displays as sj.exe.", () => Scorm12With(RawZipWriter.File("a\u202Eexe.js", "x")), v => AnyName(v, n => n.Contains('\u202E')));
        yield return Reject("name_control_character", BadNameEncoding, "U+0001 in a name.", () => Scorm12With(RawZipWriter.File("a\u0001b.js", "x")), v => AnyName(v, n => n.Contains('\u0001')));
        yield return Reject("name_bom", BadNameEncoding, "U+FEFF (byte order mark) at the start of a name.", () => Scorm12With(RawZipWriter.File("\uFEFFhidden.js", "x")), v => AnyName(v, n => n.StartsWith('\uFEFF')));
        yield return Reject("name_nul_byte", BadNameEncoding, "A NUL byte inside a name: C-string readers see a shorter name than the validator.", () => Scorm12With(new RawEntry { NameBytes = [(byte)'a', 0, (byte)'.', (byte)'j', (byte)'s'], Data = Utf8("x") }), v => AnyBytes(v, b => b.Contains((byte)0)));
        yield return Reject("name_invalid_utf8_with_flag", BadNameEncoding, "Bit 11 says UTF-8 but the bytes 0xFF 0xFE are not valid UTF-8.", () => Scorm12With(new RawEntry { NameBytes = [0xFF, 0xFE, (byte)'.', (byte)'j', (byte)'s'], Utf8Flag = true, Data = Utf8("x") }), v => v.Records.Any(r => r.Utf8Flag && r.NameBytes.Contains((byte)0xFF)));

        yield return Accept(
            "name_cp437_accented",
            "A name in CP437 without the UTF-8 flag (byte 0x82 is é): decoded as CP437, not rejected and not read as invalid UTF-8.",
            () => Scorm12With(new RawEntry { NameBytes = [(byte)'c', (byte)'a', (byte)'f', 0x82, (byte)'.', (byte)'h', (byte)'t', (byte)'m', (byte)'l'], Utf8Flag = false, Data = Utf8("x") }),
            v => v.Records.Any(r => !r.Utf8Flag && r.NameBytes.Contains((byte)0x82)),
            "The stored key is the decoded name café.html.");

        yield return Accept(
            "name_utf8_accented",
            "A name with the UTF-8 flag (é as two bytes).",
            () => Scorm12With(RawZipWriter.File("caf\u00e9.html", "x")),
            v => v.Records.Any(r => r.Utf8Flag && r.NameUtf8 == "caf\u00e9.html"));
    }

    private static IEnumerable<CorpusCase> Structure()
    {
        yield return Reject("not_a_zip_random_bytes", InvalidArchive, "Random bytes with a .zip name.", () => Enumerable.Range(0, 4096).Select(i => (byte)((i * 31 + 7) % 251)).ToArray(), _ => true, raw: true, notes: "Raw file, not a zip.");
        yield return Reject("empty_file", InvalidArchive, "Zero bytes.", () => [], _ => true, raw: true, notes: "Raw file, not a zip.");
        yield return Reject("empty_archive", InvalidArchive, "A valid zip with no entries.", () => Pack([]), v => v.Records.Count == 0);
        yield return Reject("truncated_archive", InvalidArchive, "A valid package cut in the middle of the central directory.", () => { var b = Pack(Scorm12()); return b[..^30]; }, _ => true, raw: true, notes: "Raw file, not a readable zip.");
        yield return Reject("no_manifest", ManifestInvalid, "A zip without imsmanifest.xml or h5p.json.", () => Pack([RawZipWriter.File("index.html", Sco)]), v => v.Records.All(r => r.NameUtf8 != "imsmanifest.xml"));

        yield return Accept(
            "polyglot_html_and_zip",
            "An HTML document in front of the zip data (an HTML+ZIP polyglot): the zip is read from the end and the archive is accepted; the prefix is never served.",
            () => Pack(Scorm12(), new RawZipOptions { Prefix = Utf8("<html><script>alert('polyglot')</script></html>") }),
            v => v.PrefixLength > 0,
            "A reader that scans from the start for local headers must reach the same entries.");

        yield return Accept(
            "polyglot_entry_content_types",
            "index.html that starts with GIF89a, and logo.gif that contains HTML: the stored type comes from the extension allow-list, never from the bytes or the zip.",
            () => Scorm12With(RawZipWriter.File("gif-looking.html", "GIF89a<script>alert(1)</script>"), RawZipWriter.File("html-looking.gif", "<html><script>alert(1)</script></html>")),
            v => AnyName(v, n => n == "gif-looking.html") && AnyName(v, n => n == "html-looking.gif"),
            "Served as text/html and image/gif respectively, with nosniff.");

        yield return Accept(
            "svg_with_script",
            "An SVG that contains a script: active content, served with the sandbox CSP and as an attachment on top-level navigation.",
            () => Scorm12With(RawZipWriter.File("img/logo.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")),
            v => AnyName(v, n => n.EndsWith(".svg", StringComparison.Ordinal)));
    }
}
