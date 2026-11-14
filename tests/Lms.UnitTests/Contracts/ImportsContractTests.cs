using System.Text.Json;
using System.Text.RegularExpressions;
using Lms.Shared;
using Lms.Shared.IdentityProviders;
using Lms.Shared.Imports;

namespace Lms.UnitTests.Contracts;

/// <summary>Import framework and identity provider contracts (M2 plan Task 3; D23: staff import only, D34).</summary>
public partial class ImportsContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("^[a-z][a-z-]*$")]
    private static partial Regex RouteSegment();

    [Fact]
    public void Import_kinds_are_lower_case_route_segments()
    {
        Assert.Equal(["staff", "students", "courses", "class-students"], ImportKinds.All);
        Assert.Equal(("staff", "students", "courses", "class-students"),
            (ImportKinds.Staff, ImportKinds.Students, ImportKinds.Courses, ImportKinds.ClassStudents));
        Assert.All(ImportKinds.All, k => Assert.Matches(RouteSegment(), k));
        Assert.Equal(ImportKinds.All.Count, ImportKinds.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Import_statuses_match_plan()
    {
        Assert.Equal(["Uploaded", "Validating", "Valid", "Invalid", "Committing", "Committed", "Failed"], ImportStatuses.All);
        Assert.Equal(ImportStatuses.All.Count, ImportStatuses.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Import_limits_match_global_constraints()
    {
        Assert.Equal(5000, ImportLimits.MaxRows);
        Assert.Equal(10 * 1024 * 1024, ImportLimits.MaxFileBytes);
    }

    [Fact]
    public void Identity_provider_kinds_and_match_values_are_unique()
    {
        Assert.Equal(["Oidc", "AzureAd", "Saml", "Ldap"], IdentityProviderKinds.All);
        Assert.Equal(["Email", "NationalId"], ExternalMatchBy.All);
        Assert.Equal(IdentityProviderKinds.All.Count, IdentityProviderKinds.All.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ExternalMatchBy.All.Count, ExternalMatchBy.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Import_and_identity_provider_routes_match_plan()
    {
        var id = Guid.Parse("0198a6f2-0000-7000-8000-000000000020");
        string[] patterns =
        [
            Routes.Imports, Routes.ImportKindPattern, Routes.ImportColumnsPattern, Routes.ImportTemplatePattern,
            Routes.ImportBatchPattern, Routes.ImportRowsPattern, Routes.ImportErrorReportPattern, Routes.ImportCommitPattern,
            Routes.IdentityProviders, Routes.IdentityProviderPattern, Routes.IdentityProviderTestPattern,
        ];
        Assert.Equal(patterns.Length, patterns.Distinct(StringComparer.Ordinal).Count());
        Assert.All(patterns, p => Assert.StartsWith("/api/", p, StringComparison.Ordinal));

        Assert.Equal("/api/imports", Routes.Imports);
        Assert.Equal("/api/imports/{kind}", Routes.ImportKindPattern);
        Assert.Equal("/api/imports/{kind}/columns", Routes.ImportColumnsPattern);
        Assert.Equal("/api/imports/{kind}/template", Routes.ImportTemplatePattern);
        Assert.Equal("/api/imports/{kind}/{id:guid}", Routes.ImportBatchPattern);
        Assert.Equal("/api/imports/{kind}/{id:guid}/rows", Routes.ImportRowsPattern);
        Assert.Equal("/api/imports/{kind}/{id:guid}/error-report", Routes.ImportErrorReportPattern);
        Assert.Equal("/api/imports/{kind}/{id:guid}/commit", Routes.ImportCommitPattern);
        Assert.Equal("/api/identity-providers", Routes.IdentityProviders);
        Assert.Equal("/api/identity-providers/{id:guid}", Routes.IdentityProviderPattern);
        Assert.Equal("/api/identity-providers/{id:guid}/test", Routes.IdentityProviderTestPattern);

        Assert.Equal("/api/imports/staff", Routes.ImportsOfKind(ImportKinds.Staff));
        Assert.Equal("/api/imports/staff/columns", Routes.ImportColumns(ImportKinds.Staff));
        Assert.Equal("/api/imports/staff/template?culture=ar", Routes.ImportTemplate(ImportKinds.Staff, "ar"));
        Assert.Equal($"/api/imports/staff/{id}", Routes.ImportBatch(ImportKinds.Staff, id));
        Assert.Equal($"/api/imports/staff/{id}/rows", Routes.ImportRows(ImportKinds.Staff, id));
        Assert.Equal($"/api/imports/staff/{id}/error-report", Routes.ImportErrorReport(ImportKinds.Staff, id));
        Assert.Equal($"/api/imports/staff/{id}/commit", Routes.ImportCommit(ImportKinds.Staff, id));
        Assert.Equal($"/api/identity-providers/{id}", Routes.IdentityProvider(id));
        Assert.Equal($"/api/identity-providers/{id}/test", Routes.IdentityProviderTest(id));
    }

    [Fact]
    public void Import_dtos_round_trip()
    {
        var column = new ImportColumnDto("email", "البريد الإلكتروني", "Email", true, null);
        var batch = new ImportBatchDto(Guid.NewGuid(), ImportKinds.Staff, ImportStatuses.Invalid, Guid.NewGuid(), "مدرسة", "School",
            "staff.xlsx", 3, 1, true, null, Guid.NewGuid(), "مسؤول", "Admin",
            new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc), null);
        var row = new ImportRowDto(2, new Dictionary<string, string?> { ["email"] = "x", ["mobile"] = null },
            [new ImportRowErrorDto("email", ProblemCodes.validation_failed, "البريد غير صالح")]);

        Assert.Equal(column, RoundTrip(column));
        Assert.Equal(batch, RoundTrip(batch));
        var rowBack = RoundTrip(row);
        Assert.Equal(2, rowBack.RowNumber);
        Assert.Null(rowBack.Values["mobile"]);
        Assert.Equal(row.Errors[0], Assert.Single(rowBack.Errors));
    }

    [Fact]
    public void Identity_provider_dtos_round_trip_without_a_secret()
    {
        var dto = new IdentityProviderDto(Guid.NewGuid(), IdentityProviderKinds.AzureAd, "school-a", "حساب المدرسة", "School account",
            "https://login.microsoftonline.com/t/v2.0", "t", "client", HasClientSecret: true, "openid profile email",
            ExternalMatchBy.Email, null, ["school-a.edu"], Enabled: true, "https://lms.example/signin-ext/school-a");
        var json = JsonSerializer.Serialize(dto, Web);

        Assert.DoesNotContain("\"clientSecret\"", json, StringComparison.Ordinal);
        Assert.Equal(dto.EmailDomains, RoundTrip(dto).EmailDomains);

        var save = new SaveIdentityProviderRequest(IdentityProviderKinds.Oidc, "school-b", "ب", "B", "https://idp.example", null,
            "client", null, "openid email", ExternalMatchBy.NationalId, "national_id", [], false);
        var saveBack = RoundTrip(save);
        Assert.Null(saveBack.ClientSecret);
        Assert.Equal("national_id", saveBack.NationalIdClaim);
        Assert.Equal(new ProviderTestResultDto(false, null, "timeout"), RoundTrip(new ProviderTestResultDto(false, null, "timeout")));
    }

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Web), Web)!;
}
