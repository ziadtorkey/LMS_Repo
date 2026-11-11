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

    public static string Ar(string key) => Get(Arabic.Value, key, "SharedResource.ar");

    public static string En(string key) => Get(English.Value, key, "SharedResource.en");

    /// <summary>The label in the given UI culture ("ar" or "en").</summary>
    public static string In(string culture, string key) => culture == "ar" ? Ar(key) : En(key);

    /// <summary>Server-rendered account pages (login, password reset): <c>src/Lms.Api/Resources/AccountResource.{culture}.resx</c>.</summary>
    public static string Account(string culture, string key) =>
        Get(AccountCache.GetOrAdd(culture, c => LoadFile(Path.Combine(RepoPaths.Root, "src", "Lms.Api", "Resources", $"AccountResource.{c}.resx"))),
            key, $"AccountResource.{culture}");

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> AccountCache = new();

    private static string Get(IReadOnlyDictionary<string, string> values, string key, string file) =>
        values.TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"Resource key '{key}' missing in {file}.resx");

    private static IReadOnlyDictionary<string, string> Load(string culture)
    {
        return LoadFile(Path.Combine(RepoPaths.Root, "src", "Lms.Client", "Resources", $"SharedResource.{culture}.resx"));
    }

    private static IReadOnlyDictionary<string, string> LoadFile(string path)
    {
        return XDocument.Load(path).Root!
            .Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);
    }
}
