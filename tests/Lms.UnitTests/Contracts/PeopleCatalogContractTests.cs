using System.Text.Json;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Notifications;
using Lms.Shared.People;

namespace Lms.UnitTests.Contracts;

/// <summary>M2 people contracts (M2 plan Tasks 1 and 2, scope after D23: staff and shared person DTOs only).</summary>
public class PeopleCatalogContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void New_permissions_match_plan()
    {
        Assert.Equal(
            ("Staff.View", "Staff.Create", "Staff.Update", "Staff.Delete", "Staff.Import"),
            (Permissions.Staff.View, Permissions.Staff.Create, Permissions.Staff.Update, Permissions.Staff.Delete,
                Permissions.Staff.Import));
        Assert.Equal(("IdentityProviders.View", "IdentityProviders.Manage"),
            (Permissions.IdentityProviders.View, Permissions.IdentityProviders.Manage));
        Assert.DoesNotContain(Permissions.All, p => p.StartsWith("Students.", StringComparison.Ordinal));
        Assert.Equal(Permissions.All.Count, Permissions.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void New_notification_events_have_unique_keys_and_valid_recipient_scope()
    {
        var keys = NotificationEvents.All.Select(e => e.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(["Subtree", "AncestorsAndSelf"], RecipientScopes.All);
        Assert.All(NotificationEvents.All, e => Assert.Contains(e.RecipientScope, RecipientScopes.All));

        string[] staffParameters = ["StaffNameAr", "StaffNameEn", "ExpiresOn"];
        foreach (var key in new[] { NotificationEvents.StaffExpiringSoon, NotificationEvents.StaffExpired })
        {
            var e = NotificationEvents.Find(key)!;
            Assert.Equal([BuiltInRoles.OrgAdmin, BuiltInRoles.Manager], e.DefaultRecipientRoles);
            Assert.Equal(RecipientScopes.AncestorsAndSelf, e.RecipientScope);
            Assert.Equal(staffParameters, e.Parameters);
        }

        var imports = NotificationEvents.Find(NotificationEvents.ImportsCompleted)!;
        Assert.Empty(imports.DefaultRecipientRoles);
        Assert.Equal(RecipientScopes.Subtree, imports.RecipientScope);
        Assert.Equal(["Kind", "Status", "RowCount"], imports.Parameters);

        Assert.Equal(("Staff.ExpiringSoon", "Staff.Expired", "Imports.Completed"),
            (NotificationEvents.StaffExpiringSoon, NotificationEvents.StaffExpired, NotificationEvents.ImportsCompleted));
        Assert.Equal(RecipientScopes.Subtree, NotificationEvents.Find(NotificationEvents.OrganizationPolicyChanged)!.RecipientScope);
    }

    [Fact]
    public void Catalog_values_are_unique()
    {
        Assert.Equal(["Active", "Deactivated", "Expired"], PersonStatuses.All);
        Assert.Equal(PersonStatuses.All.Count, PersonStatuses.All.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(("Active", "Deactivated", "Expired"),
            (PersonStatuses.Active, PersonStatuses.Deactivated, PersonStatuses.Expired));
    }

    [Fact]
    public void People_routes_are_unique_and_start_with_api()
    {
        var id = Guid.Parse("0198a6f2-0000-7000-8000-000000000010");
        var assignmentId = Guid.Parse("0198a6f2-0000-7000-8000-000000000011");
        string[] patterns =
        [
            Routes.Staff, Routes.StaffPattern, Routes.StaffStatusPattern, Routes.StaffSharesPattern,
            Routes.StaffSharePattern, Routes.StaffResendInvitationPattern,
        ];

        Assert.Equal(patterns.Length, patterns.Distinct(StringComparer.Ordinal).Count());
        Assert.All(patterns, p => Assert.StartsWith("/api/", p, StringComparison.Ordinal));
        Assert.Equal("/api/staff", Routes.Staff);
        Assert.Equal("/api/staff/{id:guid}", Routes.StaffPattern);
        Assert.Equal("/api/staff/{id:guid}/status", Routes.StaffStatusPattern);
        Assert.Equal("/api/staff/{id:guid}/shares", Routes.StaffSharesPattern);
        Assert.Equal("/api/staff/{id:guid}/shares/{assignmentId:guid}", Routes.StaffSharePattern);
        Assert.Equal("/api/staff/{id:guid}/resend-invitation", Routes.StaffResendInvitationPattern);

        Assert.Equal($"/api/staff/{id}", Routes.StaffMember(id));
        Assert.Equal($"/api/staff/{id}/status", Routes.StaffStatus(id));
        Assert.Equal($"/api/staff/{id}/shares", Routes.StaffShares(id));
        Assert.Equal($"/api/staff/{id}/shares/{assignmentId}", Routes.StaffShare(id, assignmentId));
        Assert.Equal($"/api/staff/{id}/resend-invitation", Routes.StaffResendInvitation(id));
    }

    [Fact]
    public void Staff_dtos_round_trip_with_arabic_and_dates()
    {
        var role = new StaffRoleDto(Guid.NewGuid(), Guid.NewGuid(), "المعلم", "Teacher", Guid.NewGuid(), "مدرسة ١", "School 1",
            new DateOnly(2026, 9, 1), null, IsShare: false);
        var summary = new StaffSummaryDto(Guid.NewGuid(), Guid.NewGuid(), "أحمد علي", "Ahmed Ali", "ahmed@example.org", "+966500000000",
            "معلم رياضيات", "Math teacher", Guid.NewGuid(), "مدرسة ١", "School 1", PersonStatuses.Active,
            new DateOnly(2027, 6, 30), null, IsSharedIn: false, [role]);
        var history = new PersonStatusChangeDto(PersonStatuses.Active, PersonStatuses.Deactivated, "إجازة",
            new DateTime(2026, 11, 11, 8, 0, 0, DateTimeKind.Utc), "المدير", "Admin");
        var detail = new StaffDetailDto(summary, "A12345", IsActivated: true, IsLimitedView: false, [history]);

        var back = RoundTrip(detail);

        Assert.Equal(summary.NameAr, back.Summary.NameAr);
        Assert.Equal(summary.ExpiresOn, back.Summary.ExpiresOn);
        Assert.Equal(role, Assert.Single(back.Summary.Roles));
        Assert.Equal(history, Assert.Single(back.StatusHistory));
        Assert.Equal("A12345", back.NationalId);

        var create = new CreateStaffRequest("أحمد", "Ahmed", "a@example.org", null, null, null, null, null, Guid.NewGuid(),
            null, [new StaffRoleInput(Guid.NewGuid(), Guid.NewGuid(), null, null)]);
        Assert.Equal(create.Roles[0], Assert.Single(RoundTrip(create).Roles));
        var update = new UpdateStaffRequest("أحمد", "Ahmed", "0500000000", null, null, null, null, Guid.NewGuid(), new DateOnly(2027, 1, 1));
        Assert.Equal(update, RoundTrip(update));
        var share = new ShareStaffRequest([new StaffRoleInput(Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2027, 1, 1))]);
        Assert.Equal(share.Targets[0], Assert.Single(RoundTrip(share).Targets));
        var status = new SetPersonStatusRequest(PersonStatuses.Deactivated, "سبب");
        Assert.Equal(status, RoundTrip(status));
    }

    [Fact]
    public void Staff_query_defaults_match_plan()
    {
        var q = new StaffQuery(null);

        Assert.True(q.IncludeDescendants);
        Assert.True(q.IncludeShared);
        Assert.Equal((1, 50), (q.Page, q.PageSize));
        Assert.Null(q.RoleId);
        Assert.Null(q.Status);
        Assert.Null(q.Search);
        Assert.Null(q.Sort);
        Assert.Equal(["name", "-name", "expiresOn", "-expiresOn"], StaffSorts.All);
    }

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Web), Web)!;
}
