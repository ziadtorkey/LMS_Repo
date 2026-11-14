using Bunit;
using Lms.Client.Services;
using Lms.Client.Tests.Support;
using Lms.Shared.Me;

namespace Lms.Client.Tests.Attendance;

public sealed class ClientTimeZoneTests : ClientTestContext
{
    private static readonly DateTime Instant = new(2026, 11, 14, 16, 0, 0, DateTimeKind.Utc); // 19:00 in Riyadh, 01:00 next day in Tokyo

    private async Task UseZoneAsync(string zoneId)
    {
        var active = Guid.NewGuid();
        await Context.SetAsync(active);
        Api.Me = Api.Me with { Contexts = [new UserContextDto(active, Guid.NewGuid(), "مدرسة", "School", Guid.NewGuid(), "الصف", "Grade", "معلم", "Teacher", zoneId)] };
    }

    private ClientTimeZone Zone() => new(Api, Context);

    [Fact]
    public async Task Without_a_readable_context_the_zone_is_Asia_Riyadh()
    {
        Assert.Equal(new DateOnly(2026, 11, 14), await Zone().TodayAsync(Instant));
        Assert.Equal(new DateOnly(2026, 11, 15), await Zone().TodayAsync(new DateTime(2026, 11, 14, 22, 0, 0, DateTimeKind.Utc))); // 01:00 in Riyadh
    }

    [Fact]
    public async Task The_zone_comes_from_the_active_context_of_GET_api_me_with_no_Settings_View()
    {
        await UseZoneAsync("Asia/Tokyo");

        Assert.Equal(new DateOnly(2026, 11, 15), await Zone().TodayAsync(Instant)); // Tokyo is already on the 15th
    }

    [Fact]
    public async Task An_unknown_zone_id_falls_back_to_Riyadh_not_UTC()
    {
        await UseZoneAsync("Nowhere/Land");

        Assert.Equal(new DateOnly(2026, 11, 15), await Zone().TodayAsync(new DateTime(2026, 11, 14, 22, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task The_setting_is_read_once_per_context()
    {
        await UseZoneAsync("Asia/Tokyo");
        var zone = Zone();
        await zone.GetAsync();
        Api.Me = Api.Me with { Contexts = [] }; // a second read would now see no context

        Assert.Equal(new DateOnly(2026, 11, 15), await zone.TodayAsync(Instant)); // still Tokyo: cached for this context
    }

    [Fact]
    public async Task LocalTime_shows_a_UTC_instant_in_the_client_zone_left_to_right()
    {
        await UseZoneAsync("Asia/Tokyo");

        var cut = Render<Lms.Client.Components.Ui.LocalTime>(p => p.Add(x => x.Utc, Instant));

        Assert.Equal("2026-11-15 01:00", cut.Find("span").TextContent); // 16:00 UTC is 01:00 on the 15th in Tokyo
        Assert.Equal("ltr", cut.Find("span").GetAttribute("dir"));
    }
}
