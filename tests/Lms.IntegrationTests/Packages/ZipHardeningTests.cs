using Lms.Infrastructure.Packages;
using Lms.IntegrationTests.Packages.Corpus;
using Lms.Shared.Packages;

namespace Lms.IntegrationTests.Packages;

/// <summary>
/// The crafted corpus (#470, <c>Packages/Corpus</c>) through <see cref="ZipHardening"/> (M4b Task 8a): every archive-level defect is
/// refused with its intended reason, every well-formed or content-level case passes (manifest, XML and H5P JSON rules are the
/// validation job's, Task 8b), and extraction cannot yield more than was inspected.
/// </summary>
public sealed class ZipHardeningTests
{
    /// <summary>Reasons the archive reader decides; the other reject reasons belong to the validation job.</summary>
    private static readonly HashSet<string> ArchiveLevel =
    [
        PackageRejectReasons.unsafe_path, PackageRejectReasons.too_many_entries, PackageRejectReasons.too_large,
        PackageRejectReasons.compression_ratio, PackageRejectReasons.symlink, PackageRejectReasons.duplicate_name,
        PackageRejectReasons.bad_name_encoding, PackageRejectReasons.executable, PackageRejectReasons.encrypted,
        PackageRejectReasons.nested_archive, PackageRejectReasons.invalid_archive, PackageRejectReasons.bad_name,
    ];

    public static TheoryData<string> Rejected => [.. CorpusCases.All.Where(c => c.Kind == CorpusKind.Reject && ArchiveLevel.Contains(c.Expect)).Select(c => c.Name)];

    public static TheoryData<string> Passing => [.. CorpusCases.All.Where(c => c.Kind != CorpusKind.Reject || !ArchiveLevel.Contains(c.Expect)).Select(c => c.Name)];

    /// <summary>The caps the case's notes name: only the rule under test may fire.</summary>
    private static ZipHardeningLimits LimitsFor(string name) => name switch
    {
        "bomb_total_many_entries" => new ZipHardeningLimits { MaxUncompressedBytes = 100L * 1024 * 1024 },
        "bomb_lying_header" => new ZipHardeningLimits { MaxUncompressedBytes = 40L * 1024 * 1024, MaxEntryRatio = 1_000_000 },
        _ => new ZipHardeningLimits(),
    };

    private static async Task<ZipInspection> InspectAsync(string name)
    {
        var c = CorpusCases.All.Single(x => x.Name == name);
        await using var stream = new FileStream(CorpusCases.CommittedFile(c), FileMode.Open, FileAccess.Read, FileShare.Read);
        return await ZipHardening.InspectAsync(stream, LimitsFor(name));
    }

    [Theory]
    [MemberData(nameof(Rejected))]
    public async Task Archive_level_defect_is_rejected_with_its_reason(string name)
    {
        var expected = CorpusCases.All.Single(c => c.Name == name).Expect;

        var result = await InspectAsync(name);

        Assert.Equal(expected, result.RejectReason);
    }

    [Theory]
    [MemberData(nameof(Passing))]
    public async Task Well_formed_and_content_level_cases_pass_the_archive_reader(string name)
    {
        var result = await InspectAsync(name);

        Assert.True(result.Accepted, $"{name} was rejected as {result.RejectReason}: {result.Detail}");
        Assert.NotEmpty(result.Entries);
    }

    [Fact]
    public async Task Zip_slip_entry_is_rejected() => Assert.Equal("unsafe_path", (await InspectAsync("zip_slip_dotdot")).RejectReason);

    [Fact]
    public async Task Decompression_bomb_is_rejected_by_real_bytes_not_headers()
    {
        // The headers say 100 bytes; only counting the stream finds the 50 MB.
        var result = await InspectAsync("bomb_lying_header");

        Assert.Equal("too_large", result.RejectReason);
        Assert.Equal("compression_ratio", (await InspectAsync("bomb_ratio_one_entry")).RejectReason);
    }

    [Fact]
    public async Task A_header_that_lies_about_the_size_is_refused_even_under_the_caps()
    {
        var c = CorpusCases.All.Single(x => x.Name == "bomb_lying_header");
        await using var stream = new FileStream(CorpusCases.CommittedFile(c), FileMode.Open, FileAccess.Read, FileShare.Read);

        var result = await ZipHardening.InspectAsync(stream, new ZipHardeningLimits { MaxEntryRatio = 1_000_000 });

        Assert.Equal("invalid_archive", result.RejectReason);
    }

    [Fact]
    public async Task Zip64_is_read_with_64_bit_counters()
    {
        var result = await InspectAsync("zip64_valid_small");

        Assert.True(result.Accepted);
        Assert.Equal(["imsmanifest.xml", "index.html"], result.Entries.Select(e => e.Name).Order(StringComparer.Ordinal));
        Assert.All(result.Entries, e => Assert.True(e.Length > 0));
        Assert.Equal("too_large", (await InspectAsync("zip64_declared_huge")).RejectReason);
    }

    [Fact]
    public async Task Polyglot_zip_html_is_read_from_the_end_and_names_stay_literal()
    {
        var polyglot = await InspectAsync("polyglot_html_and_zip");
        var literal = await InspectAsync("percent_encoded_dotdot_is_a_literal_name");

        Assert.True(polyglot.Accepted);
        Assert.Contains(polyglot.Entries, e => e.Name == "index.html");
        Assert.Contains(literal.Entries, e => e.Name == "%2e%2e%2fnot-a-parent.js");
    }

    [Fact]
    public async Task Cp437_and_utf8_names_decode_to_the_same_text()
    {
        var cp437 = await InspectAsync("name_cp437_accented");
        var utf8 = await InspectAsync("name_utf8_accented");

        Assert.Contains(cp437.Entries, e => e.Name == "café.html");
        Assert.Contains(utf8.Entries, e => e.Name == "café.html");
    }

    [Fact]
    public void Cp437_table_covers_the_upper_half()
    {
        var decoded = ZipNameRules.Decode(Enumerable.Range(0x80, 128).Select(i => (byte)i).ToArray(), utf8Flag: false);

        Assert.Equal(128, decoded!.Length);
        Assert.Equal('Ç', decoded[0]);
        Assert.Equal('é', decoded[2]);
        Assert.Equal(' ', decoded[127]);
    }

    private static Task<ZipInspection> InspectAsync(params string[] names) =>
        InspectBytesAsync(Lms.IntegrationTests.Packages.Corpus.RawZipWriter.Write(names.Select(n => Lms.IntegrationTests.Packages.Corpus.RawZipWriter.File(n, "x"))));

    private static async Task<ZipInspection> InspectBytesAsync(byte[] bytes) => await ZipHardening.InspectAsync(new MemoryStream(bytes), new ZipHardeningLimits());

    [Theory]
    [InlineData("a\u061Cb.js", "bad_name_encoding")]
    [InlineData("a\u2028b.js", "bad_name_encoding")]
    [InlineData("a\u00ADb.js", "bad_name_encoding")]
    [InlineData("a\u180Eb.js", "bad_name_encoding")]
    [InlineData("page.php.txt", "executable")]
    [InlineData("tool.PHAR", "executable")]
    [InlineData("run.php5", "executable")]
    [InlineData("a.vbs", "executable")]
    [InlineData("a.hta", "executable")]
    [InlineData("a.py", "executable")]
    [InlineData("file.txt:stream", "unsafe_path")]
    public async Task Extra_name_and_extension_rules_refuse_the_name(string name, string reason)
    {
        var result = await InspectAsync("imsmanifest.xml", name);

        Assert.Equal(reason, result.RejectReason);
    }

    [Fact]
    public async Task A_whole_path_over_the_store_key_budget_is_refused()
    {
        var path = string.Join('/', Enumerable.Range(0, 10).Select(_ => new string('a', 120))) + "/f.js";

        Assert.Equal("bad_name", (await InspectAsync("imsmanifest.xml", path)).RejectReason);
    }

    [Theory]
    [InlineData("a", "a/b.txt")]
    [InlineData("a/b.txt", "a")]
    [InlineData("A", "a/B.txt")]
    public async Task A_file_and_a_folder_of_the_same_name_collide(string first, string second)
    {
        var result = await InspectAsync(first, second);

        Assert.Equal("duplicate_name", result.RejectReason);
    }

    [Fact]
    public async Task Sibling_files_and_folders_with_similar_names_do_not_collide()
    {
        var result = await InspectAsync("a/b.txt", "a/c.txt", "ab.txt", "a2/b.txt");

        Assert.True(result.Accepted, result.Detail);
    }

    [Fact]
    public async Task Extraction_yields_exactly_the_inspected_bytes_of_each_entry()
    {
        var c = CorpusCases.All.Single(x => x.Name == "scorm12_valid");
        await using var stream = new FileStream(CorpusCases.CommittedFile(c), FileMode.Open, FileAccess.Read, FileShare.Read);
        var inspection = await ZipHardening.InspectAsync(stream, new ZipHardeningLimits());
        var seen = new Dictionary<string, long>();

        await ZipHardening.ExtractAsync(stream, inspection, async (entry, content) =>
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy);
            seen[entry.Name] = copy.Length;
        });

        Assert.Equal(inspection.Entries.ToDictionary(e => e.Name, e => e.Length), seen);
    }

    [Fact]
    public async Task Extraction_stops_when_an_entry_is_longer_than_inspected()
    {
        var c = CorpusCases.All.Single(x => x.Name == "scorm12_valid");
        await using var stream = new FileStream(CorpusCases.CommittedFile(c), FileMode.Open, FileAccess.Read, FileShare.Read);
        var inspection = await ZipHardening.InspectAsync(stream, new ZipHardeningLimits());
        var shorter = inspection with { Entries = [.. inspection.Entries.Select(e => e with { Length = e.Length - 1 })] };

        var ex = await Assert.ThrowsAsync<ZipHardeningException>(() =>
            ZipHardening.ExtractAsync(stream, shorter, async (_, content) => await content.CopyToAsync(Stream.Null)));

        Assert.Equal("too_large", ex.Reason);
    }

    [Fact]
    public async Task Extraction_of_a_rejected_inspection_is_refused()
    {
        await using var stream = new MemoryStream();
        var rejected = ZipInspection.Rejected("unsafe_path");

        await Assert.ThrowsAsync<InvalidOperationException>(() => ZipHardening.ExtractAsync(stream, rejected, (_, _) => Task.CompletedTask));
    }
}
