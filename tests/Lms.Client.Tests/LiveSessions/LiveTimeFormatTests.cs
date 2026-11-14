using System.Globalization;
using Lms.Client.Resources;
using Lms.Client.Services;
using Lms.Client.Services.LiveSessions;
using Lms.Client.Tests.Support;
using Lms.Shared.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Tests.LiveSessions;

public sealed class LiveTimeFormatTests : ClientTestContext
{
    private IStringLocalizer<SharedResource> Localizer(string culture)
    {
        Culture.Current = new CultureInfo(culture);
        return Services.GetRequiredService<IStringLocalizer<SharedResource>>();
    }

    [Theory]
    [InlineData(1, "ar", "بعد دقيقة")]
    [InlineData(2, "ar", "بعد دقيقتين")]
    [InlineData(8, "ar", "بعد 8 دقائق")]
    [InlineData(15, "ar", "بعد 15 دقيقة")]
    [InlineData(130, "ar", "بعد ساعتين و10 دقائق")]
    [InlineData(60 * 24 * 2 + 180, "ar", "بعد يومين و3 ساعات")]
    [InlineData(8, "en", "in 8 min")]
    [InlineData(130, "en", "in 2 h 10 min")]
    [InlineData(60 * 24 * 2 + 180, "en", "in 2 d 3 h")]
    public void Countdown_uses_the_largest_two_units_with_the_right_plural(int minutes, string culture, string expected)
    {
        var l = Localizer(culture);

        var text = LiveTimeFormat.Describe(TimeSpan.FromMinutes(minutes), l, arabic: culture == "ar");

        Assert.Equal(expected, (culture == "ar" ? "بعد " : "in ") + text);
    }

    [Theory]
    [InlineData("en", "less than a minute")]
    [InlineData("ar", "أقل من دقيقة")]
    public void Less_than_a_minute(string culture, string expected) =>
        Assert.Equal(expected, LiveTimeFormat.Describe(TimeSpan.FromSeconds(30), Localizer(culture), culture == "ar"));

    [Fact]
    public void Reminder_shows_its_utc_time_as_local_time_in_an_ltr_isolate()
    {
        var l = Localizer("ar");
        var startsAt = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
        var local = startsAt.ToLocalTime();
        var notification = new NotificationDto(Guid.NewGuid(), NotificationEvents.LiveReminder,
            new Dictionary<string, string> { ["TitleAr"] = "الكسور", ["TitleEn"] = "Fractions", ["CourseNameAr"] = "الرياضيات", ["CourseNameEn"] = "Maths", ["StartsAtUtc"] = startsAt.ToString("O") },
            null, startsAt, false);

        var sameDay = NotificationText.Format(notification, l, new CultureInfo("ar"), localNow: local);
        var otherDay = NotificationText.Format(notification, l, new CultureInfo("ar"), localNow: local.AddDays(3));

        Assert.Contains("\u2066" + local.ToString("HH:mm", CultureInfo.InvariantCulture) + "\u2069", sameDay);
        Assert.Contains("\u2066" + local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "\u2069", otherDay);
        Assert.DoesNotContain("T10:00", sameDay);
    }
}
