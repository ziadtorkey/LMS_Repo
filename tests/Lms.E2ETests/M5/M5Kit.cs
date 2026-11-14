using System.Text.Json;
using Lms.E2ETests.Infrastructure;

namespace Lms.E2ETests.M5;

/// <summary>
/// API-side helpers of the M5 demos: reading the grading scales of a unit (plan #621 Task 4, contract #625). The gradebook itself is driven through the
/// UI; these helpers only prepare data and check what the API reports.
/// </summary>
public static class M5Kit
{
    public sealed record Scale(Guid Id, string NameEn, string Kind, bool IsDefault, bool IsTemplate, bool Provisional, string? SeedKey, Guid? InheritedFromUnitId, int BandCount);

    /// <summary>The scales visible at the unit (its own and its ancestors', the latter read only).</summary>
    public static async Task<IReadOnlyList<Scale>> ScalesAsync(E2EApi api, Guid orgUnitId)
    {
        var response = await api.SendAsync("GET", $"/api/org-units/{orgUnitId}/grading-scales");
        if (!response.Ok)
        {
            throw new InvalidOperationException($"Reading the grading scales answered {response.Status}: {await response.TextAsync()}");
        }

        return (await api.JsonAsync(response)).EnumerateArray().Select(s => new Scale(
            s.GetProperty("id").GetGuid(),
            s.GetProperty("nameEn").GetString()!,
            s.GetProperty("kind").GetString()!,
            s.GetProperty("isDefault").GetBoolean(),
            s.GetProperty("isTemplate").GetBoolean(),
            s.GetProperty("provisional").GetBoolean(),
            s.TryGetProperty("seedKey", out var key) && key.ValueKind == JsonValueKind.String ? key.GetString() : null,
            s.TryGetProperty("inheritedFromUnitId", out var inherited) && inherited.ValueKind == JsonValueKind.String ? inherited.GetGuid() : null,
            s.GetProperty("bands").GetArrayLength())).ToList();
    }

    /// <summary>The root unit of the signed-in admin's client (scales are provisioned there, L7).</summary>
    public static async Task<Guid> RootUnitAsync(E2EApi api)
    {
        var units = await api.JsonAsync(await api.SendAsync("GET", "/api/org-units"));
        return units.EnumerateArray().Single(u => u.GetProperty("parentId").ValueKind == JsonValueKind.Null).GetProperty("id").GetGuid();
    }
}
