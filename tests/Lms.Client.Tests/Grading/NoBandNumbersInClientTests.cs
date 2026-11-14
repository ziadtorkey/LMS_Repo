using System.Text.RegularExpressions;

namespace Lms.Client.Tests.Grading;

/// <summary>
/// The Saudi presets (their bands, labels and Arabic descriptors) are data the server serves (L7, L23): nothing about a preset may be written into the
/// client's source or resources. Only the in-memory fake carries demo bands.
/// </summary>
public sealed class NoBandNumbersInClientTests
{
    private static string ClientRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Lms.Client"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "Lms.Client");
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(ClientRoot(), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor") || f.EndsWith(".cs") || f.EndsWith(".resx"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => !Path.GetFileName(f).StartsWith("Fake", StringComparison.Ordinal));

    [Fact]
    public void No_preset_seed_key_or_band_descriptor_is_written_in_client_source_or_resources()
    {
        var hits = SourceFiles()
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"sa-percentage|sa-letter|SeedKey\s*==\s*""|Excellent|Very good|ممتاز|جيد جدا"))
            .Select(f => Path.GetRelativePath(ClientRoot(), f))
            .ToList();

        Assert.Empty(hits);
    }

    [Fact]
    public void No_resource_key_names_a_preset_or_a_band_label()
    {
        var keys = SourceFiles().Where(f => f.EndsWith(".resx"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), "data name=\"([^\"]+)\"").Select(m => m.Groups[1].Value))
            .Where(k => Regex.IsMatch(k, @"^(Scale|Band|Preset)_(Preset|Saudi|Letter|Percent)", RegexOptions.IgnoreCase) || k.Contains("Saudi", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(keys);
    }
}
