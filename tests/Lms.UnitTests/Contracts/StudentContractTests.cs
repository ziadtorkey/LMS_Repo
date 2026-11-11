using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lms.Shared;
using Lms.Shared.Imports;
using Lms.Shared.People;

namespace Lms.UnitTests.Contracts;

/// <summary>M3 contract (plan Task 2): student, guardian and change-request catalogs, DTOs and routes (M2 appendix + D24/D45).</summary>
public sealed partial class StudentContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex TwoLetters();

    public static TheoryData<string, string[]> Catalogs => new()
    {
        { nameof(Genders), [.. Genders.All] },
        { nameof(Religions), [.. Religions.All] },
        { nameof(GuardianRelations), [.. GuardianRelations.All] },
        { nameof(StudentCodeModes), [.. StudentCodeModes.All] },
        { nameof(StudentFields), [.. StudentFields.All] },
        { nameof(ChangeRequestStatuses), [.. ChangeRequestStatuses.All] },
        { nameof(StudentSorts), [.. StudentSorts.All] },
        { nameof(CredentialsDelivery), [.. CredentialsDelivery.All] },
    };

    [Theory]
    [MemberData(nameof(Catalogs))]
    public void Catalog_values_are_unique(string catalog, string[] values)
    {
        Assert.NotEmpty(values);
        Assert.True(values.Length == values.Distinct(StringComparer.Ordinal).Count(), $"{catalog} has duplicates.");
    }

    [Fact]
    public void Catalogs_match_the_plan()
    {
        Assert.Equal(["Male", "Female"], Genders.All);
        Assert.Equal(["Muslim", "Christian", "Other"], Religions.All);
        Assert.Equal(["Father", "Mother", "Brother", "Sister", "Grandfather", "Grandmother", "Uncle", "Aunt", "Other"],
            GuardianRelations.All);
        Assert.Equal(["Auto", "Manual"], StudentCodeModes.All);
        Assert.Equal(["Pending", "Approved", "Rejected", "Cancelled"], ChangeRequestStatuses.All);
        Assert.Equal(["name", "-name", "code", "-code"], StudentSorts.All);
        Assert.Equal(["Student", "PrimaryGuardian", "None"], CredentialsDelivery.All);
        Assert.Equal(13, StudentFields.All.Count);
    }

    [Fact]
    public void Requestable_fields_exclude_StudentCode_and_Email()
    {
        Assert.DoesNotContain(StudentFields.StudentCode, StudentFields.Requestable);
        Assert.DoesNotContain(StudentFields.Email, StudentFields.Requestable);
        Assert.Equal(StudentFields.All.Except([StudentFields.StudentCode, StudentFields.Email]), StudentFields.Requestable);
    }

    [Fact]
    public void Default_critical_fields_are_requestable()
    {
        Assert.Equal(["NameAr", "NameEn", "DateOfBirth", "Gender", "Religion", "Nationality", "NationalId"], StudentFields.DefaultCritical);
        Assert.All(StudentFields.DefaultCritical, f => Assert.Contains(f, StudentFields.Requestable));
    }

    [Fact]
    public void Nationalities_have_two_letter_codes_and_both_names()
    {
        Assert.Equal(249, Nationalities.All.Count);
        Assert.Equal(Nationalities.All.Count, Nationalities.All.Select(n => n.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.All(Nationalities.All, n =>
        {
            Assert.Matches(TwoLetters(), n.Code);
            Assert.False(string.IsNullOrWhiteSpace(n.NameAr));
            Assert.False(string.IsNullOrWhiteSpace(n.NameEn));
            Assert.Matches(@"\p{IsArabic}", n.NameAr);
        });
        Assert.Equal(("المملكة العربية السعودية", "Saudi Arabia"), (Nationalities.Find("SA")!.NameAr, Nationalities.Find("SA")!.NameEn));
        Assert.Equal("Egypt", Nationalities.Find("EG")!.NameEn);
        Assert.Null(Nationalities.Find("sa"));
        Assert.Null(Nationalities.Find("XX"));
        Assert.Null(Nationalities.Find(null));
    }

    [Fact]
    public void Import_column_dto_has_sensitive_flag_defaulting_to_false()
    {
        var plain = new ImportColumnDto("email", "البريد", "Email", false, null);
        var secret = new ImportColumnDto("defaultPassword", "كلمة المرور", "Default password", false, null, Sensitive: true);

        Assert.False(plain.Sensitive);
        Assert.True(RoundTrip(secret).Sensitive);
        Assert.False(JsonSerializer.Deserialize<ImportColumnDto>(
            """{"key":"email","headerAr":"ب","headerEn":"E","required":false,"allowedValues":null}""", Web)!.Sensitive);
        Assert.Equal("••••••", ImportMasks.Sensitive);
        var last = typeof(ImportColumnDto).GetConstructors().Single().GetParameters().Last();
        Assert.Equal((nameof(ImportColumnDto.Sensitive), true), (last.Name, last.HasDefaultValue));
    }

    [Fact]
    public void Only_StudentCreatedDto_has_a_password_property()
    {
        var passwordProps = typeof(Routes).Assembly.GetExportedTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => (Type: t, Prop: p)))
            .Where(x => x.Prop.PropertyType == typeof(string)
                && x.Prop.Name.Contains("Password", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Requests may carry a password the admin types in; no response other than StudentCreatedDto ever does (D24).
        var responses = passwordProps.Where(x => !x.Type.Name.EndsWith("Request", StringComparison.Ordinal))
            .Select(x => $"{x.Type.Name}.{x.Prop.Name}").ToList();
        Assert.Equal(["StudentCreatedDto.IssuedPassword"], responses);
        Assert.Contains(passwordProps, x => x.Type == typeof(CreateStudentRequest));
        Assert.Contains(passwordProps, x => x.Type == typeof(ResetStudentPasswordRequest));
    }

    [Fact]
    public void People_routes_are_unique_and_start_with_api()
    {
        var id = Guid.Parse("0198a6f2-0000-7000-8000-000000000003");
        var other = Guid.Parse("0198a6f2-0000-7000-8000-000000000004");

        Assert.Equal("/api/students", Routes.Students);
        Assert.Equal("/api/students/{id:guid}/reset-password", Routes.StudentResetPasswordPattern);
        Assert.Equal("/api/students/{id:guid}/guardians/{guardianId:guid}", Routes.StudentGuardianPattern);
        Assert.Equal("/api/guardians/match", Routes.GuardiansMatch);
        Assert.Equal("/api/guardians/{id:guid}/portal-invitation", Routes.GuardianPortalInvitationPattern);
        Assert.Equal("/api/student-change-requests/{id:guid}/approve", Routes.StudentChangeRequestApprovePattern);
        Assert.Equal("/api/me/student-profile/change-requests/{id:guid}", Routes.MyChangeRequestPattern);
        Assert.Equal("/api/me/children", Routes.MyChildren);

        Assert.Equal($"/api/students/{id}/feature-flags", Routes.StudentFeatureFlags(id));
        Assert.Equal($"/api/students/{id}/guardians/{other}", Routes.StudentGuardian(id, other));
        Assert.Equal($"/api/students/{id}/reset-password", Routes.StudentResetPassword(id));
        Assert.Equal($"/api/guardians/{id}/portal-invitation", Routes.GuardianPortalInvitation(id));
        Assert.Equal($"/api/student-change-requests/{id}/reject", Routes.StudentChangeRequestReject(id));
        Assert.Equal($"/api/me/student-profile/change-requests/{id}", Routes.MyChangeRequest(id));
        Assert.DoesNotContain(RouteConstants(), r => r.EndsWith("/students/{id:guid}/resend-invitation", StringComparison.Ordinal));

        AssertUniqueApiRoutes();
    }

    [Fact]
    public void Student_dtos_round_trip()
    {
        var data = new StudentDataInput("أحمد", "Ahmed", null, null, new DateOnly(2015, 3, 1), Genders.Male, Religions.Muslim, "SA",
            "1012345678", null, null, null);
        var create = new CreateStudentRequest(data, Guid.NewGuid(), null, false,
            [new GuardianLinkInput(null, new GuardianDataInput("محمد", "Mohammed", "+966500000123", null, null), GuardianRelations.Father, true)],
            ClassId: Guid.NewGuid());
        var summary = new StudentSummaryDto(Guid.NewGuid(), "000001", "أحمد", "Ahmed", data.DateOfBirth, Genders.Male, Guid.NewGuid(),
            "مدرسة", "School", PersonStatuses.Active, null, "محمد", "Mohammed", "+966500000123", Guid.NewGuid(), "١/أ", "1/A");
        var detail = new StudentDetailDto(summary, null, null, Religions.Muslim, "SA", null, null, null, true, false, true,
            new Dictionary<string, bool> { ["Features.Messaging"] = true }, [], [], 0);
        var created = new StudentCreatedDto(detail, "Xy7!pass-word", CredentialsDelivery.PrimaryGuardian);

        Assert.Null(create.DefaultPassword);
        Assert.Equal(create.ClassId, RoundTrip(create).ClassId);
        Assert.Equal("1/A", RoundTrip(summary).CurrentClassNameEn);
        Assert.True(RoundTrip(detail).MustChangePassword);
        Assert.Equal(CredentialsDelivery.PrimaryGuardian, RoundTrip(created).CredentialsSentTo);
        var query = new StudentQuery(null, Unplaced: true);
        Assert.Equal((Paging.DefaultPageSize, true, null), (query.PageSize, RoundTrip(query).Unplaced, query.ClassId));
        Assert.Null(RoundTrip(new SetStudentFeatureFlagsRequest(new Dictionary<string, bool?> { ["Features.Messaging"] = null }))
            .Overrides["Features.Messaging"]);
        Assert.Null(new ChangeRequestQuery(null).Status);
        Assert.Equal("+*********123", RoundTrip(new GuardianMatchDto(Guid.NewGuid(), "محمد", "Mohammed", "+*********123", 2)).MaskedMobile);
    }

    internal static IReadOnlyList<string> RouteConstants() =>
        typeof(Routes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

    internal static void AssertUniqueApiRoutes()
    {
        var routes = RouteConstants();
        Assert.All(routes, r => Assert.StartsWith("/api/", r, StringComparison.Ordinal));
        var duplicates = routes.GroupBy(r => r, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
    }

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Web), Web)!;
}
