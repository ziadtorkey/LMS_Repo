using System.Text.Json;
using Lms.Shared;
using Lms.Shared.Organizations;

namespace Lms.UnitTests.Contracts;

public class OrganizationContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Organization_routes_match_plan()
    {
        var id = Guid.Parse("0198a6f2-0000-7000-8000-000000000001");

        Assert.Equal("/api/clients", Routes.Clients);
        Assert.Equal("/api/level-types", Routes.LevelTypes);
        Assert.Equal("/api/level-types/order", Routes.LevelTypesOrder);
        Assert.Equal("/api/org-units", Routes.OrgUnits);

        Assert.Equal("/api/clients/{id:guid}", Routes.ClientPattern);
        Assert.Equal("/api/level-types/{id:guid}", Routes.LevelTypePattern);
        Assert.Equal("/api/org-units/{id:guid}", Routes.OrgUnitPattern);
        Assert.Equal("/api/org-units/{id:guid}/status", Routes.OrgUnitStatusPattern);
        Assert.Equal("/api/org-units/{id:guid}/move", Routes.OrgUnitMovePattern);
        Assert.Equal("/api/org-units/{id:guid}/academic-flags", Routes.OrgUnitAcademicFlagsPattern);

        Assert.Equal($"/api/clients/{id}", Routes.Client(id));
        Assert.Equal($"/api/level-types/{id}", Routes.LevelType(id));
        Assert.Equal($"/api/org-units/{id}", Routes.OrgUnit(id));
        Assert.Equal($"/api/org-units/{id}/status", Routes.OrgUnitStatus(id));
        Assert.Equal($"/api/org-units/{id}/move", Routes.OrgUnitMove(id));
        Assert.Equal($"/api/org-units/{id}/academic-flags", Routes.OrgUnitAcademicFlags(id));
    }

    [Fact]
    public void Status_and_type_constants_match_plan()
    {
        Assert.Equal(("SchoolGroup", "University"), (OrganizationTypes.SchoolGroup, OrganizationTypes.University));
        Assert.Equal(("Active", "Suspended"), (OrganizationStatuses.Active, OrganizationStatuses.Suspended));
        Assert.Equal(("Active", "Inactive"), (OrgUnitStatuses.Active, OrgUnitStatuses.Inactive));
        Assert.Equal(["SchoolGroup", "University"], OrganizationTypes.All);
        Assert.Equal(["Active", "Suspended"], OrganizationStatuses.All);
        Assert.Equal(["Active", "Inactive"], OrgUnitStatuses.All);
    }

    [Fact]
    public void Paging_limits_match_plan()
    {
        Assert.Equal(50, Paging.DefaultPageSize);
        Assert.Equal(200, Paging.MaxPageSize);
    }

    [Fact]
    public void PagedResult_of_ClientDto_round_trips_with_arabic_names()
    {
        var client = new ClientDto(Guid.NewGuid(), "مجموعة مدارس التجمع", "Altagamo3 Schools", OrganizationTypes.SchoolGroup,
            OrganizationStatuses.Active, Guid.NewGuid(), new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc));
        var page = new PagedResult<ClientDto>([client], 1, 1, Paging.DefaultPageSize);

        var json = JsonSerializer.Serialize(page, Web);
        var back = JsonSerializer.Deserialize<PagedResult<ClientDto>>(json, Web)!;

        Assert.Equal(client, Assert.Single(back.Items));
        Assert.Equal((1, 1, 50), (back.Total, back.Page, back.PageSize));
        Assert.Contains("\"rootOrgUnitId\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void OrgUnitNodeDto_round_trips_with_blocked_permissions()
    {
        var node = new OrgUnitNodeDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "الروضة", "KG", "KG-1", 2,
            OrgUnitStatuses.Active, true, Guid.NewGuid(), false, true, ["OrgUnits.Create"]);

        var back = JsonSerializer.Deserialize<OrgUnitNodeDto>(JsonSerializer.Serialize(node, Web), Web)!;

        Assert.Equal(node.Id, back.Id);
        Assert.Equal(node.NextTransferUnitId, back.NextTransferUnitId);
        Assert.Equal(node.NameAr, back.NameAr);
        Assert.Equal(node.BlockedPermissions, back.BlockedPermissions);
    }

    [Fact]
    public void Organization_requests_round_trip()
    {
        var create = new CreateClientRequest("جامعة", "University A", OrganizationTypes.University, "الجامعة", "University",
            "UNI", "admin@example.test", "مدير", "Admin");
        var levelType = new SaveLevelTypeRequest("مدرسة", "School", true);
        var reorder = new ReorderLevelTypesRequest([Guid.NewGuid(), Guid.NewGuid()]);
        var flags = new OrgUnitAcademicFlagsRequest(true, null, true);

        Assert.Equal(create, RoundTrip(create));
        Assert.Equal(levelType, RoundTrip(levelType));
        Assert.Equal(reorder.OrderedIds, RoundTrip(reorder).OrderedIds);
        Assert.Equal(flags, RoundTrip(flags));
        Assert.Equal(new MoveOrgUnitRequest(reorder.OrderedIds[0]), RoundTrip(new MoveOrgUnitRequest(reorder.OrderedIds[0])));
    }

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Web), Web)!;
}
