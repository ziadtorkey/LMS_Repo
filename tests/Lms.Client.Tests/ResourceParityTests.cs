using System.Xml.Linq;

namespace Lms.Client.Tests;

public sealed class ResourceParityTests
{
    private static HashSet<string> Keys(string lang)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Lms.Client"))) dir = Path.GetDirectoryName(dir);
        var path = Path.Combine(dir!, "src", "Lms.Client", "Resources", $"SharedResource.{lang}.resx");
        return XDocument.Load(path).Descendants("data").Select(d => (string)d.Attribute("name")!).ToHashSet();
    }

    [Fact]
    public void Arabic_and_english_resources_have_identical_keys() =>
        Assert.True(Keys("ar").SetEquals(Keys("en")));
}
