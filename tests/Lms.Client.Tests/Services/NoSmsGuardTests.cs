using System.Text.RegularExpressions;
using Lms.Shared.Configuration;

namespace Lms.Client.Tests.Services;

/// <summary>D76: no SMS in v1. Nothing in the labels or the setting catalog may bring it back by accident.</summary>
public sealed partial class NoSmsGuardTests
{
    private static string Resources()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Lms.Client"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir!, "src", "Lms.Client", "Resources");
    }

    [GeneratedRegex("<data name=\"([^\"]+)\"")]
    private static partial Regex KeyPattern();

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void No_resource_key_mentions_sms(string culture)
    {
        var keys = KeyPattern().Matches(File.ReadAllText(Path.Combine(Resources(), $"SharedResource.{culture}.resx"))).Select(m => m.Groups[1].Value);

        Assert.DoesNotContain(keys, k => k.Contains("Sms", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void No_catalog_setting_mentions_sms()
    {
        Assert.DoesNotContain(SettingCatalog.All, d => d.Name.Contains("Sms", StringComparison.OrdinalIgnoreCase));
    }
}
