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

    [Fact]
    public void Placeholders_match_between_arabic_and_english()
    {
        // A {0} or {Name} that exists in one language only breaks the sentence at run time (string.Format / the notification template).
        var ar = Load("ar");
        var en = Load("en");
        var placeholder = new System.Text.RegularExpressions.Regex(@"\{(\w+)\}");
        string[] Names(string text) => placeholder.Matches(text).Select(m => m.Groups[1].Value).Distinct().Order().ToArray();

        // Deliberate differences: the Arabic pager text shows only the total, and the Arabic singular and dual forms of a counted
        // unit ("ساعة", "ساعتين") carry no number.
        string[] allowed = ["Table_InfoFormat", .. new[] { "Day", "Hour", "Minute" }.SelectMany(u => new[] { "One", "Two" }.Select(f => $"Live_Unit_{u}_{f}"))];

        var mismatches = en.Keys.Where(ar.ContainsKey).Where(k => !allowed.Contains(k))
            .Where(k => !Names(en[k]).SequenceEqual(Names(ar[k])))
            .Select(k => $"{k}: en [{string.Join(",", Names(en[k]))}] ar [{string.Join(",", Names(ar[k]))}]")
            .ToList();

        Assert.Empty(mismatches);
    }
}
