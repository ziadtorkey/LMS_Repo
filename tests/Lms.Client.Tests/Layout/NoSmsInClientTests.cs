using System.Reflection;
using System.Text.RegularExpressions;
using Lms.Client.Services;

namespace Lms.Client.Tests.Layout;

/// <summary>
/// D76: no SMS in v1. No type, member, resource key or source identifier of the client may model an SMS channel, so it cannot come back by
/// accident. The vendored editor (wwwroot/lib) is not ours and is not scanned.
/// </summary>
public sealed partial class NoSmsInClientTests
{
    private static string ClientDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Lms.Client"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir!, "src", "Lms.Client");
    }

    private static bool MentionsSms(string name) => name.Contains("Sms", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void No_client_type_or_member_mentions_sms()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var found = typeof(CultureService).Assembly.GetTypes()
            .SelectMany(t => new[] { t.FullName ?? t.Name }.Concat(t.GetMembers(all).Select(m => $"{t.Name}.{m.Name}")))
            .Where(MentionsSms)
            .ToList();

        Assert.Empty(found);
    }

    [GeneratedRegex("<data name=\"([^\"]+)\"")]
    private static partial Regex KeyPattern();

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void No_resource_key_mentions_sms(string culture)
    {
        var resx = File.ReadAllText(Path.Combine(ClientDir(), "Resources", $"SharedResource.{culture}.resx"));

        Assert.DoesNotContain(KeyPattern().Matches(resx).Select(m => m.Groups[1].Value), MentionsSms);
    }

    [GeneratedRegex(@"\b\w*Sms\w*\b")]
    private static partial Regex SmsIdentifier();

    [Fact]
    public void No_client_source_uses_an_sms_identifier()
    {
        var lib = Path.Combine(ClientDir(), "wwwroot", "lib") + Path.DirectorySeparatorChar;
        var found = Directory.EnumerateFiles(ClientDir(), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".js", StringComparison.Ordinal))
            .Where(f => !f.StartsWith(lib, StringComparison.OrdinalIgnoreCase) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => SmsIdentifier().Matches(File.ReadAllText(f)).Select(m => $"{Path.GetFileName(f)}: {m.Value}"))
            .ToList();

        Assert.Empty(found);
    }
}
