using System.Xml.Linq;
using Lms.Client.Services;
using Lms.Shared.Notifications;

namespace Lms.Client.Tests;

/// <summary>Every event of the catalog (so every wave added later too) has its message template and its label in both languages.</summary>
public sealed class NotificationLabelParityTests
{
    private static Dictionary<string, string> Load(string lang)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Lms.Client"))) dir = Path.GetDirectoryName(dir);
        var path = Path.Combine(dir!, "src", "Lms.Client", "Resources", $"SharedResource.{lang}.resx");
        return XDocument.Load(path).Descendants("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Notification_types_have_both_languages(string lang)
    {
        var resources = Load(lang);
        var missing = new List<string>();
        foreach (var e in NotificationEvents.All)
        {
            foreach (var key in new[] { NotificationText.TemplateKey(e.Key), NotificationText.EventLabelKey(e.Key) })
                if (!resources.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) missing.Add(key);
        }

        Assert.Empty(missing);
    }
}
