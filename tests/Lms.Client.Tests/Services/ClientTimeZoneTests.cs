using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Me;

namespace Lms.Client.Tests.Services;

/// <summary>The client time zone (D64): the zone of the active context, Asia/Riyadh when it is missing, unknown or unreadable.</summary>
public sealed class ClientTimeZoneTests : ClientTestContext
{
    private static UserContextDto Ctx(Guid assignment, string zone) =>
        new(assignment, Guid.NewGuid(), "م", "Org", Guid.NewGuid(), "و", "Unit", "معلم", "Teacher", zone);

    private ClientTimeZone Zone() => new(Api, Context);

    [Fact]
    public async Task Reads_the_zone_of_the_active_context()
    {
        var id = Guid.NewGuid();
        await Context.SetAsync(id);
        Api.Me = Api.Me with { Contexts = [Ctx(Guid.NewGuid(), "Asia/Dubai"), Ctx(id, "Africa/Cairo")] };

        Assert.Equal("Africa/Cairo", (await Zone().GetAsync()).Id);
    }

    [Theory]
    [InlineData("Not/AZone")]
    [InlineData("")]
    public async Task An_unknown_or_empty_zone_falls_back_to_riyadh(string zone)
    {
        var id = Guid.NewGuid();
        await Context.SetAsync(id);
        Api.Me = Api.Me with { Contexts = [Ctx(id, zone)] };

        Assert.Equal(ClientCalendar.Zone().Id, (await Zone().GetAsync()).Id);
    }

    [Fact]
    public async Task An_unreadable_me_falls_back_to_riyadh()
    {
        await Context.SetAsync(Guid.NewGuid());
        Api.Failure = new HttpRequestException("offline");

        Assert.Equal(ClientCalendar.Zone().Id, (await Zone().GetAsync()).Id);
    }

    [Fact]
    public async Task Switching_context_reads_the_new_zone()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Api.Me = Api.Me with { Contexts = [Ctx(a, "Asia/Dubai"), Ctx(b, "Africa/Cairo")] };
        var zone = Zone();
        await Context.SetAsync(a);
        Assert.Equal("Asia/Dubai", (await zone.GetAsync()).Id);

        await Context.SetAsync(b);

        Assert.Equal("Africa/Cairo", (await zone.GetAsync()).Id);
    }

    [Fact]
    public async Task Today_is_the_client_local_day_not_the_utc_day()
    {
        await Context.SetAsync(Guid.NewGuid());

        var today = await Zone().TodayAsync(new DateTime(2026, 11, 17, 22, 0, 0, DateTimeKind.Utc)); // 01:00 on the 18th in Riyadh

        Assert.Equal(new DateOnly(2026, 11, 18), today);
    }
}
