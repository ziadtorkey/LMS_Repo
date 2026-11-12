using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.M2;
using System.Text.Json;

namespace Lms.E2ETests.M3;

/// <summary>Set-up through the API for the M3 scenarios: a current academic year and classes at the seeded KG section.</summary>
internal static class M3Kit
{
    private const string YearNameAr = "العام E2E 2027/2028";
    private const string YearNameEn = "E2E Year 2027/2028";

    /// <summary>The seeded KG Section 1 (a section hosts students and classes) and the school's current academic year.</summary>
    public sealed record KgSection(Guid UnitId, Guid YearId);

    public sealed record Placement(Guid ClassId, string NameAr, string NameEn);

    /// <summary>
    /// Finds the KG stage and the active year of the school group, defining and activating the shared E2E year when the
    /// database has none (the M1 year scenarios define the same year, so both reuse it).
    /// </summary>
    public static async Task<KgSection> EnsureKgSectionAsync(E2EApi api)
    {
        var units = await api.JsonAsync(await api.SendAsync("GET", "/api/org-units"));
        Guid Unit(string code) => units.EnumerateArray().Single(u => u.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
        var school = Unit("S");
        var kg = Unit("KG-S1");

        var effective = await api.JsonAsync(await api.SendAsync("GET", $"/api/academic-years/effective?orgUnitId={kg}"));
        if (effective.GetProperty("currentYearId").ValueKind == JsonValueKind.String)
        {
            return new KgSection(kg, effective.GetProperty("currentYearId").GetGuid());
        }

        var existing = effective.GetProperty("years").EnumerateArray()
            .FirstOrDefault(y => y.GetProperty("nameEn").GetString() == YearNameEn);
        var yearId = existing.ValueKind == JsonValueKind.Object
            ? existing.GetProperty("id").GetGuid()
            : (await api.JsonAsync(await api.SendAsync("POST", "/api/academic-years",
                new { orgUnitId = school, nameAr = YearNameAr, nameEn = YearNameEn, startDate = "2027-09-01", endDate = "2028-06-30" }))).GetProperty("id").GetGuid();
        var activated = await api.SendAsync("POST", $"/api/academic-years/{yearId}/activate");
        if (!activated.Ok)
        {
            throw new InvalidOperationException($"Activating the E2E year answered {activated.Status}: {await activated.TextAsync()}");
        }

        return new KgSection(kg, yearId);
    }

    /// <summary>Creates a class at KG Section 1 in the current year with a name that carries the run's suffix, so the picker finds it.</summary>
    public static async Task<Placement> CreateClassAsync(E2EApi api, KgSection kg, M2Names names, int? capacity)
    {
        var nameAr = names.NameAr("فصل");
        var nameEn = names.NameEn("Class");
        var created = await api.JsonAsync(await api.SendAsync("POST", "/api/classes",
            new { code = $"E2E-{names.Suffix}".ToUpperInvariant(), nameAr, nameEn, orgUnitId = kg.UnitId, academicYearId = kg.YearId, capacity, homeroomTeacherUserId = (Guid?)null }));
        return new Placement(created.GetProperty("id").GetGuid(), nameAr, nameEn);
    }

    public sealed record StudentAccount(Guid Id, string NameAr, string NameEn, string UserName, string Password);

    /// <summary>
    /// Creates a student at KG Section 1 through the API with a new primary guardian (so the credentials go to the guardian)
    /// and returns the student with the sign-in name and the one-time default password.
    /// </summary>
    public static async Task<StudentAccount> CreateStudentAsync(E2EApi api, KgSection kg, M2Names names, string label, int slot)
    {
        var nameAr = names.NameAr($"{label}-ar");
        var nameEn = names.NameEn(label);
        var created = await api.JsonAsync(await api.SendAsync("POST", "/api/students", new
        {
            data = new
            {
                nameAr, nameEn, nicknameAr = (string?)null, nicknameEn = (string?)null, dateOfBirth = $"{DateTime.Today.Year - 8}-03-15",
                gender = "Male", religion = "Muslim", nationality = "EG", nationalId = names.NationalId(slot),
                mobile = (string?)null, email = (string?)null, photoFileId = (Guid?)null,
            },
            homeOrgUnitId = kg.UnitId,
            studentCode = (string?)null,
            confirmPossibleDuplicate = false,
            guardians = new[]
            {
                new
                {
                    existingGuardianId = (Guid?)null,
                    newGuardian = new { nameAr = names.NameAr($"ولي-{label}"), nameEn = names.NameEn($"Guardian-{label}"), mobile = names.MobileFor(slot), email = names.Email($"g{slot}"), nationalId = names.NationalId(slot + 50) },
                    relation = "Father",
                    isPrimary = true,
                },
            },
            classId = (Guid?)null,
            defaultPassword = (string?)null,
            confirmOverCapacity = false,
        }));
        return new StudentAccount(
            created.GetProperty("student").GetProperty("summary").GetProperty("id").GetGuid(), nameAr, nameEn,
            created.GetProperty("userName").GetString()!, created.GetProperty("issuedPassword").GetString()!);
    }
}
