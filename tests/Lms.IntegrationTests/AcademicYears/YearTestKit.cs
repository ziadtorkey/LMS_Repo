using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.AcademicYears;
using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.AcademicYears;
using Lms.Shared.Authorization;

namespace Lms.IntegrationTests.AcademicYears;

internal static class YearTestKit
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public static async Task<HttpResponseMessage> SendAsync(
        LmsApiFactory factory, HttpMethod method, string url, RoleAssignment assignment, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserHeader, assignment.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, assignment.Id.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Web);
        }

        return await factory.CreateClient().SendAsync(request);
    }

    public static SaveAcademicYearRequest YearRequest(OrgUnit unit, string name, DateOnly start, DateOnly end) =>
        new(unit.Id, "عام " + name, "Year " + name, start, end);

    public static async Task<AcademicYearDto> CreateYearAsync(
        LmsApiFactory factory, RoleAssignment admin, OrgUnit unit, string name, DateOnly start, DateOnly end)
    {
        var response = await SendAsync(factory, HttpMethod.Post, "/api/academic-years", admin, YearRequest(unit, name, start, end));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AcademicYearDto>(Web))!;
    }

    public static async Task<YearContextDto> EffectiveAsync(LmsApiFactory factory, RoleAssignment who, OrgUnit unit)
    {
        var response = await SendAsync(factory, HttpMethod.Get, $"/api/academic-years/effective?orgUnitId={unit.Id}", who);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<YearContextDto>(Web))!;
    }

    /// <summary>Seeds a year directly (any status), as another module or the M7 close flow would.</summary>
    public static async Task<AcademicYear> SeedYearAsync(
        LmsApiFactory factory, OrgUnit unit, DateOnly start, DateOnly end, AcademicYearStatus status)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var year = new AcademicYear(unit, "عام", "Seeded", start, end);
        if (status == AcademicYearStatus.Active)
        {
            year.Activate();
        }

        if (status == AcademicYearStatus.Closed)
        {
            year.Close();
        }

        db.AcademicYears.Add(year);
        await db.SaveChangesAsync();
        return year;
    }
}
