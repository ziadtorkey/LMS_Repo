using System.Text.RegularExpressions;

namespace Lms.Client.Tests;

/// <summary>Guards against framework utility classes we do not ship (they silently do nothing: a screen-reader-only text would show on screen). A general "every lms- class is defined" scan is not possible: many lms- classes are deliberate markup hooks without a rule.</summary>
public sealed class CssClassHygieneTests
{
    private static string ClientRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Lms.Client"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("repo root"), "src", "Lms.Client");
    }

    private static IEnumerable<string> RazorFiles() =>
        Directory.EnumerateFiles(ClientRoot(), "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void Screen_reader_only_text_uses_lms_visually_hidden()
    {
        // Bootstrap's "visually-hidden" and "sr-only" are not part of this app: only .lms-visually-hidden exists, and anything else shows the text on screen.
        var bad = RazorFiles()
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"class=""[^""]*(?<![\w-])(visually-hidden|sr-only)(?![\w-])"))
            .Select(f => Path.GetRelativePath(ClientRoot(), f)).ToList();

        Assert.True(bad.Count == 0, "use lms-visually-hidden (not visually-hidden / sr-only) in: " + string.Join(", ", bad));
    }
}
