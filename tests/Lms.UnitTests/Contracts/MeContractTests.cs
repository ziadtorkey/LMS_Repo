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
    public void EffectivePermissionsDto_round_trips_through_web_json()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var dto = new EffectivePermissionsDto(Guid.NewGuid(), ["OrgUnits.View"]);

        var back = JsonSerializer.Deserialize<EffectivePermissionsDto>(JsonSerializer.Serialize(dto, options), options)!;

        Assert.Equal(dto.AssignmentId, back.AssignmentId);
        Assert.Equal(dto.Permissions, back.Permissions);
    }
}
