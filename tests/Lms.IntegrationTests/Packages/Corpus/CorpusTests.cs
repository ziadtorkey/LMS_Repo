using System.Security.Cryptography;
using System.Text.Json;

namespace Lms.IntegrationTests.Packages.Corpus;

/// <summary>
/// The corpus guards itself: every committed archive still holds the defect it is named for (checked on the raw bytes, independent
/// of the code under test), the index is in step with the files, and the attack classes of the plan are all present.
/// </summary>
public sealed class CorpusTests
{
    public static TheoryData<string> CaseNames => [.. CorpusCases.All.Select(c => c.Name)];

    private static CorpusCase Case(string name) => CorpusCases.All.Single(c => c.Name == name);

    [Fact]
    public void Case_names_are_unique_and_file_safe()
    {
        var names = CorpusCases.All.Select(c => c.Name).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, n => Assert.Matches("^[a-z0-9_]+$", n));
    }

    [Fact]
    public void Every_expectation_is_a_catalog_reject_reason_or_accepted()
    {
        foreach (var c in CorpusCases.All)
        {
            if (c.Kind == CorpusKind.Reject)
            {
                Assert.Contains(c.Expect, CorpusReasons.All);
            }
            else
            {
                Assert.Equal(CorpusCase.Accepted, c.Expect);
            }
        }
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Committed_archive_still_contains_its_defect(string name)
    {
        var c = Case(name);
        var path = CorpusCases.CommittedFile(c);
        Assert.True(File.Exists(path), $"{c.FileName} is missing; regenerate with LMS_REGEN_CORPUS=1.");
        var bytes = File.ReadAllBytes(path);

        if (c.Raw)
        {
            Assert.ThrowsAny<Exception>(() => RawZipView.Parse(bytes));
            return;
        }

        var view = RawZipView.Parse(bytes);
        Assert.True(c.Has(view), $"{c.FileName} no longer contains the defect: {c.Intent}");
    }

    [Fact]
    public void Index_matches_the_committed_files()
    {
        var dir = Path.GetDirectoryName(CorpusCases.CommittedFile(CorpusCases.All[0]))!;
        var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json"))).RootElement.EnumerateArray().ToList();

        Assert.Equal(CorpusCases.All.Count, index.Count);
        foreach (var entry in index)
        {
            var file = Path.Combine(dir, entry.GetProperty("file").GetString()!);
            Assert.Equal(entry.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant());
        }
    }

    [Fact]
    public void Light_cases_regenerate_byte_for_byte()
    {
        foreach (var c in CorpusCases.All.Where(c => !c.Heavy))
        {
            Assert.True(c.Build().SequenceEqual(File.ReadAllBytes(CorpusCases.CommittedFile(c))), $"{c.FileName} differs from its builder; regenerate with LMS_REGEN_CORPUS=1.");
        }
    }

    [Fact]
    public void Every_attack_class_of_the_plan_is_present()
    {
        string[] required =
        [
            "zip_slip_dotdot", "zip_slip_backslash", "absolute_unix", "absolute_drive_letter", "unc_path", "local_name_differs_from_central",
            "bomb_ratio_one_entry", "bomb_total_many_entries", "bomb_lying_header", "too_many_entries", "zip64_valid_small", "zip64_declared_huge",
            "symlink_entry", "encrypted_entry", "executable_php", "executable_exe", "nested_archive_zip", "nested_archive_tgz",
            "duplicate_case_fold", "duplicate_nfc_nfd", "name_bidi_override", "name_control_character", "name_bom", "name_nul_byte",
            "name_cp437_accented", "name_utf8_accented", "device_name_con", "polyglot_html_and_zip", "polyglot_entry_content_types", "svg_with_script",
            "xxe_file_entity", "xxe_http_entity", "xml_billion_laughs", "manifest_schema_location_and_xml_base_never_fetched",
            "manifest_href_parent_escape", "multi_sco", "launch_frameset", "launch_iframe_to_local_page",
            "h5p_json_deeply_nested", "h5p_json_huge", "html_utf8_bom", "html_utf16le_bom", "html_meta_charset_latin1", "html_no_head",
            "sco_defines_its_own_window_api", "sco_catches_the_parent_walk_error", "sco_meta_refresh_outside", "sco_touches_storage", "sco_xhr_own_host",
            "scorm12_valid", "scorm2004_valid", "h5p_valid_minimal",
        ];

        var present = CorpusCases.All.Select(c => c.Name).ToHashSet();
        var missing = required.Where(r => !present.Contains(r)).ToList();
        Assert.True(missing.Count == 0, "Missing corpus cases: " + string.Join(", ", missing));
    }

    /// <summary>Regenerates the archives, the index and the README table: <c>LMS_REGEN_CORPUS=1 dotnet test --filter CorpusFiles</c>. A no-op otherwise.</summary>
    [Fact]
    public void CorpusFiles_are_regenerated_on_request()
    {
        if (Environment.GetEnvironmentVariable("LMS_REGEN_CORPUS") != "1")
        {
            return;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "Lms.IntegrationTests", "Packages", "Corpus")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var target = Path.Combine(dir.FullName, "tests", "Lms.IntegrationTests", "Packages", "Corpus", "files");
        Directory.CreateDirectory(target);
        var built = CorpusCases.All.ToDictionary(c => c.Name, c => c.Build());
        foreach (var c in CorpusCases.All)
        {
            File.WriteAllBytes(Path.Combine(target, c.FileName), built[c.Name]);
        }

        File.WriteAllText(Path.Combine(target, "manifest.json"), CorpusCases.ManifestJson(c => built[c.Name]));
    }
}
