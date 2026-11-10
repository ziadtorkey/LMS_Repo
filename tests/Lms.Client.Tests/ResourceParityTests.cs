using System.Xml.Linq;

namespace Lms.Client.Tests;

public sealed class ResourceParityTests
{
    private static Dictionary<string, string> Load(string lang)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Lms.Client"))) dir = Path.GetDirectoryName(dir);
        var path = Path.Combine(dir!, "src", "Lms.Client", "Resources", $"SharedResource.{lang}.resx");
        return XDocument.Load(path).Descendants("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
    }

    [Fact]
    public void Every_key_exists_in_both_arabic_and_english()
    {
        var ar = Load("ar").Keys.ToHashSet();
        var en = Load("en").Keys.ToHashSet();

        Assert.Empty(ar.Except(en));
        Assert.Empty(en.Except(ar));
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void No_resource_value_is_empty(string lang) =>
        Assert.Empty(Load(lang).Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key));
}
