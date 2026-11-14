using System.Text.Json;
using Lms.Shared;
using Lms.Shared.Me;

namespace Lms.UnitTests.Contracts;

public class MeContractTests
{
    [Fact]
    public void Routes_match_plan()
    {
        Assert.Equal("/api/me", Routes.Me);
        Assert.Equal("/api/me/permissions", Routes.MyPermissions);
    }

    [Fact]
    public void MeDto_round_trips_through_web_json_with_arabic_names()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var context = new UserContextDto(Guid.NewGuid(), Guid.NewGuid(), "مجموعة مدارس التجمع", "Altagamo3 Schools",
            Guid.NewGuid(), "الروضة", "KG", "المعلم", "Teacher");
        var me = new MeDto(Guid.NewGuid(), "أحمد", "Ahmed", false, [context]);

        var back = JsonSerializer.Deserialize<MeDto>(JsonSerializer.Serialize(me, options), options)!;

        Assert.Equal(me.UserId, back.UserId);
        Assert.Equal(context, Assert.Single(back.Contexts));
    }

    [Fact]
    public void A_context_carries_the_client_time_zone_with_the_riyadh_default()
    {
        // D64: the zone rides with the contexts every signed-in user loads (no Settings.View needed); the default keeps older payloads valid.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var context = new UserContextDto(Guid.NewGuid(), Guid.NewGuid(), "م", "O", Guid.NewGuid(), "و", "U", "د", "R");
        Assert.Equal("Asia/Riyadh", context.TimeZone);
        Assert.Equal(Lms.Shared.Configuration.SettingKeys.OrgTimeZone.Default, context.TimeZone);

        var cairo = context with { TimeZone = "Africa/Cairo" };
        var back = JsonSerializer.Deserialize<UserContextDto>(JsonSerializer.Serialize(cairo, options), options)!;
        Assert.Equal("Africa/Cairo", back.TimeZone);
        Assert.Contains("\"timeZone\":\"Africa/Cairo\"", JsonSerializer.Serialize(cairo, options));
        Assert.Equal("Asia/Riyadh", JsonSerializer.Deserialize<UserContextDto>(
            "{\"assignmentId\":\"" + Guid.Empty + "\",\"organizationId\":\"" + Guid.Empty + "\",\"organizationNameAr\":\"\",\"organizationNameEn\":\"\",\"orgUnitId\":\"" + Guid.Empty + "\",\"orgUnitNameAr\":\"\",\"orgUnitNameEn\":\"\",\"roleNameAr\":\"\",\"roleNameEn\":\"\"}",
            options)!.TimeZone);
    }

    [Fact]
    public void EffectivePermissionsDto_round_trips_through_web_json()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var dto = new EffectivePermissionsDto(Guid.NewGuid(), ["OrgUnits.View"]);

        var back = JsonSerializer.Deserialize<EffectivePermissionsDto>(JsonSerializer.Serialize(dto, options), options)!;

        Assert.Equal(dto.AssignmentId, back.AssignmentId);
        Assert.Equal(dto.Permissions, back.Permissions);
    }
}
