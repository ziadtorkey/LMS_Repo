using System.Xml.Linq;

namespace Lms.E2ETests.Infrastructure;

/// <summary>
/// Expected UI text read from the client's resource files, so the tests assert the real Arabic and English labels
/// without hard-coding them.
/// </summary>
public static class Resx
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Arabic = new(() => Load("ar"));
    private static readonly Lazy<IReadOnlyDictionary<string, string>> English = new(() => Load("en"));

    public static string Ar(string key) => Get(Arabic.Value, key, "ar");

    public static string En(string key) => Get(English.Value, key, "en");

    private static string Get(IReadOnlyDictionary<string, string> values, string key, string culture) =>
        values.TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"Resource key '{key}' missing in SharedResource.{culture}.resx");

    private static IReadOnlyDictionary<string, string> Load(string culture)
    {
        var path = Path.Combine(RepoPaths.Root, "src", "Lms.Client", "Resources", $"SharedResource.{culture}.resx");
        return XDocument.Load(path).Root!
            .Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);
    }
}
