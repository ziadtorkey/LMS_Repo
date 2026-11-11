using System.Globalization;
using System.Xml.Linq;
using Lms.Client.Resources;
using Lms.Client.Services;
using Lms.Client.Tests.Support;
using Lms.Shared.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Tests.Services;

public sealed class NotificationTextTests : ClientTestContext
{
    private static NotificationDto Assigned() => new(Guid.NewGuid(), NotificationEvents.AccessRoleAssigned,
        new Dictionary<string, string> { ["RoleNameEn"] = "Teacher", ["RoleNameAr"] = "المعلم", ["OrgUnitNameEn"] = "Grade 1", ["OrgUnitNameAr"] = "الصف الأول" },
        null, DateTime.UtcNow, false);

    private string Format(NotificationDto n, string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        return NotificationText.Format(n, Services.GetRequiredService<IStringLocalizer<SharedResource>>(), new CultureInfo(culture));
    }

    [Fact]
    public void Text_uses_arabic_parameters_in_arabic_culture()
    {
        Assert.Equal("تم منحك دور المعلم في الصف الأول.", Format(Assigned(), "ar"));
        Assert.Equal("You were given the role Teacher at Grade 1.", Format(Assigned(), "en"));
    }

    [Fact]
    public void Unknown_event_falls_back_to_a_generic_text()
    {
        var unknown = Assigned() with { EventKey = "Future.Event" };

        Assert.Equal("New notification", Format(unknown, "en"));
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Every_catalog_event_has_ar_and_en_template(string lang)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Lms.Client"))) dir = Path.GetDirectoryName(dir);
        var keys = XDocument.Load(Path.Combine(dir!, "src", "Lms.Client", "Resources", $"SharedResource.{lang}.resx"))
            .Descendants("data").Select(d => (string)d.Attribute("name")!).ToHashSet();

        foreach (var e in NotificationEvents.All)
        {
            Assert.Contains(NotificationText.TemplateKey(e.Key), keys);
            Assert.Contains(NotificationText.EventLabelKey(e.Key), keys);
        }
    }
}
