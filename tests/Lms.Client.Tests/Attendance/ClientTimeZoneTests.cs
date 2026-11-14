using Lms.Client.Services;
using Lms.Client.Tests.Support;
using Lms.Shared.Configuration;
using Lms.Shared.Me;

namespace Lms.Client.Tests.Attendance;

public sealed class ClientTimeZoneTests : ClientTestContext
{
    private static readonly DateTime Instant = new(2026, 11, 14, 16, 0, 0, DateTimeKind.Utc); // 19:00 in Riyadh, 01:00 next day in Tokyo

    private async Task UseSettingAsync(string zoneJson)
    {
        var active = Guid.NewGuid();
        await Context.SetAsync(active);
        Api.Me = Api.Me with { Contexts = [new UserContextDto(active, Guid.NewGuid(), "مدرسة", "School", Guid.NewGuid(), "الصف", "Grade", "معلم", "Teacher")] };
        Config.Settings.RemoveAll(s => s.Name == SettingKeys.OrgTimeZone.Name);
        Config.Settings.Add(new EffectiveSettingDto(SettingKeys.OrgTimeZone.Name, zoneJson, false, false, false, null, null, null));
    }

    private ClientTimeZone Zone() => new(Api, Config, Context);

    [Fact]
    public async Task Without_a_readable_context_the_zone_is_Asia_Riyadh()
    {
        Assert.Equal(new DateOnly(2026, 11, 14), await Zone().TodayAsync(Instant));
        Assert.Equal(new DateOnly(2026, 11, 15), await Zone().TodayAsync(new DateTime(2026, 11, 14, 22, 0, 0, DateTimeKind.Utc))); // 01:00 in Riyadh
    }

    [Fact]
    public async Task The_clients_Org_TimeZone_setting_is_read_from_the_context_unit()
    {
        await UseSettingAsync("\"Asia/Tokyo\"");

        Assert.Equal(new DateOnly(2026, 11, 15), await Zone().TodayAsync(Instant)); // Tokyo is already on the 15th
    }

    [Fact]
    public async Task An_unknown_zone_id_falls_back_to_Riyadh_not_UTC()
    {
        await UseSettingAsync("\"Nowhere/Land\"");

        Assert.Equal(new DateOnly(2026, 11, 15), await Zone().TodayAsync(new DateTime(2026, 11, 14, 22, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task The_setting_is_read_once_per_context()
    {
        await UseSettingAsync("\"Asia/Tokyo\"");
        var zone = Zone();
        await zone.GetAsync();
        Config.Settings.RemoveAll(s => s.Name == SettingKeys.OrgTimeZone.Name); // a second read would now see the default

        Assert.Equal(new DateOnly(2026, 11, 15), await zone.TodayAsync(Instant)); // still Tokyo: cached for this context
    }
}
