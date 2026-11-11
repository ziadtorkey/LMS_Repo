using System.Text.Json;
using Lms.Domain.Auditing;
using Lms.Shared;
using Lms.Shared.Access;
using Lms.Shared.Auditing;
using Lms.Shared.Authorization;

namespace Lms.UnitTests.Contracts;

public class AccessContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Audit_catalog_entity_types_are_unique()
    {
        Assert.Equal(AuditCatalog.EntityTypes.Count, AuditCatalog.EntityTypes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
        [
            "Organization", "LevelType", "OrgUnit", "Role", "RolePermission", "RoleAssignment", "OrgUnitSetting",
            "OrgUnitPolicy", "User", "OrgTemplate", "AcademicYear", "Term", "CalendarEvent", "StoredFile",
            "StaffProfile", "StudentProfile", "GuardianProfile", "StudentGuardian", "ImportBatch", "IdentityProviderConfig",
            "StudentChangeRequest", "Course", "CourseAllocation", "CourseRestriction", "CourseSupervisor", "Blueprint",
            "Class", "ClassStudent", "ClassCourse", "ClassCourseTeacher", "TeacherCourseEligibility", "CourseEnrollment",
            "EnrollmentException", "StudentGroup", "GroupMember", "StudentTransfer",
        ], AuditCatalog.EntityTypes);
    }

    [Fact]
    public void Security_events_include_the_M0_login_events()
    {
        Assert.Contains("LoginSucceeded", AuditCatalog.SecurityEvents);
        Assert.Contains("LoginFailed", AuditCatalog.SecurityEvents);
        Assert.Equal(
            ["LoginSucceeded", "LoginFailed", "PasswordResetRequested", "PasswordReset", "InvitationAccepted",
             "ExternalLoginLinked", "ExternalLoginSucceeded", "ExternalLoginFailed"],
            AuditCatalog.SecurityEvents);
        Assert.Equal(
            ("ExternalLoginLinked", "ExternalLoginSucceeded", "ExternalLoginFailed"),
            (AuditSecurityEvents.ExternalLoginLinked, AuditSecurityEvents.ExternalLoginSucceeded, AuditSecurityEvents.ExternalLoginFailed));
        Assert.Equal(AuditCatalog.SecurityEvents.Count, AuditCatalog.SecurityEvents.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(AuditCatalog.SecurityEvents.Intersect(AuditCatalog.EntityTypes, StringComparer.Ordinal));
        Assert.Equal("PasswordResetRequested", AuditSecurityEvents.PasswordResetRequested);
    }

    [Fact]
    public void Audit_actions_match_the_domain_enum_names()
    {
        string[] expected = [AuditActions.Created, AuditActions.Updated, AuditActions.Deleted, AuditActions.SecurityEvent];
        Assert.Equal(Enum.GetNames<AuditAction>(), expected);
        Assert.Equal(Enum.GetNames<AuditAction>(), AuditActions.All);
    }

    [Fact]
    public void Audit_export_formats_match_the_query_values()
    {
        Assert.Equal(("csv", "xlsx"), (AuditExportFormats.Csv, AuditExportFormats.Xlsx));
    }

    [Fact]
    public void Permission_groups_hold_every_assignable_permission_once_and_no_platform_only()
    {
        var flattened = PermissionGroups.Assignable.SelectMany(g => g.Permissions).ToList();

        Assert.Equal(
            Permissions.All.Except(Permissions.PlatformOnly).Order(StringComparer.Ordinal),
            flattened.Order(StringComparer.Ordinal));
        Assert.All(PermissionGroups.Assignable, g =>
            Assert.All(g.Permissions, p => Assert.StartsWith(g.Module + ".", p, StringComparison.Ordinal)));
        Assert.Equal(PermissionGroups.Assignable.Count,
            PermissionGroups.Assignable.Select(g => g.Module).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(PermissionGroups.Assignable, g => g.Module == "Clients");
    }

    [Fact]
    public void Access_and_audit_routes_match_plan()
    {
        var id = Guid.Parse("0198a6f2-0000-7000-8000-000000000003");

        Assert.Equal("/api/permissions", Routes.Permissions);
        Assert.Equal("/api/roles", Routes.Roles);
        Assert.Equal("/api/assignments", Routes.Assignments);
        Assert.Equal("/api/audit", Routes.Audit);
        Assert.Equal("/api/audit/export", Routes.AuditExport);

        Assert.Equal("/api/roles/{id:guid}", Routes.RolePattern);
        Assert.Equal("/api/assignments/{id:guid}", Routes.AssignmentPattern);
        Assert.Equal("/api/org-units/{id:guid}/assignments", Routes.OrgUnitAssignmentsPattern);
        Assert.Equal("/api/audit/{id:guid}", Routes.AuditEntryPattern);

        Assert.Equal($"/api/roles/{id}", Routes.Role(id));
        Assert.Equal($"/api/assignments/{id}", Routes.Assignment(id));
        Assert.Equal($"/api/org-units/{id}/assignments", Routes.OrgUnitAssignments(id));
        Assert.Equal($"/api/audit/{id}", Routes.AuditEntry(id));
    }

    [Fact]
    public void AuditQuery_defaults_to_first_page_of_fifty()
    {
        var query = new AuditQuery(null, null, null, null, null, null, null);

        Assert.Equal((1, Paging.DefaultPageSize), (query.Page, query.PageSize));
    }

    [Fact]
    public void Role_and_assignment_dtos_round_trip_with_arabic_and_dates()
    {
        var role = new RoleDto(Guid.NewGuid(), null, "مسؤول الاختبارات", "Exam Officer", RoleBaseKinds.Staff, false,
            [Permissions.Audit.View], 3);
        var assignment = new AssignmentDto(Guid.NewGuid(), Guid.NewGuid(), "t@example.test", "أحمد", "Ahmed",
            Guid.NewGuid(), "المعلم", "Teacher", Guid.NewGuid(), "الروضة", "KG",
            new DateOnly(2026, 9, 1), null, true);
        var create = new CreateAssignmentRequest("t@example.test", null, null, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 30));

        var roleBack = RoundTrip(role);
        Assert.Equal((role.NameAr, role.BuiltInKey, role.AssignmentCount), (roleBack.NameAr, roleBack.BuiltInKey, roleBack.AssignmentCount));
        Assert.Equal(role.Permissions, roleBack.Permissions);
        Assert.Equal(assignment, RoundTrip(assignment));
        Assert.Equal(create, RoundTrip(create));
        Assert.Equal(new UpdateAssignmentRequest(null, new DateOnly(2027, 1, 1)),
            RoundTrip(new UpdateAssignmentRequest(null, new DateOnly(2027, 1, 1))));
        Assert.Equal(["Roles.View"], RoundTrip(new UpdateRoleRequest("أ", "A", ["Roles.View"])).Permissions);
        Assert.Equal(RoleBaseKinds.Staff, RoundTrip(new CreateRoleRequest("أ", "A", RoleBaseKinds.Staff, [])).BaseKind);
    }

    [Fact]
    public void Audit_detail_round_trips_with_arabic_json_intact()
    {
        var entry = new AuditEntryDto(Guid.NewGuid(), new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc), Guid.NewGuid(),
            "a@example.test", "أحمد", "Ahmed", "مسؤول النظام", "Admin", "الروضة", "KG", AuditActions.Updated,
            "OrgUnit", Guid.NewGuid(), "203.0.113.7");
        var detail = new AuditEntryDetailDto(entry, "{\"NameAr\":\"قديم\"}", "{\"NameAr\":\"جديد\"}");

        var back = RoundTrip(detail);

        Assert.Equal(entry, back.Entry);
        Assert.Equal(detail.AfterJson, back.AfterJson);
    }

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Web), Web)!;
}
