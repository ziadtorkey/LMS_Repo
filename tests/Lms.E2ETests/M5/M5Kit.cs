using System.Text.Json;
using Lms.E2ETests.Infrastructure;
using Lms.E2ETests.M2;

namespace Lms.E2ETests.M5;

/// <summary>
/// API-side helpers of the M5 demos (plan #621 Tasks 4, 8, 11; contract #625). The gradebook itself is driven through the UI; these helpers prepare
/// data, read what the API reports, and publish where the publish dialog (Task 27) is not on main yet. JSON is the camelCase of the contract DTOs.
/// </summary>
public static class M5Kit
{
    public sealed record Scale(Guid Id, string NameEn, string Kind, bool IsDefault, bool IsTemplate, bool Provisional, string? SeedKey, Guid? InheritedFromUnitId, int BandCount);

    /// <summary>The scales visible at the unit (its own and its ancestors', the latter read only).</summary>
    public static async Task<IReadOnlyList<Scale>> ScalesAsync(E2EApi api, Guid orgUnitId)
    {
        var response = await api.SendAsync("GET", $"/api/org-units/{orgUnitId}/grading-scales");
        Ok(response.Ok, "reading the grading scales", response.Status, await response.TextAsync());
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

    /// <summary>The school unit of the shared E2E tree (code "S"), where the guardian portal flag is set like in the M3 demo.</summary>
    public static async Task<Guid> SchoolUnitAsync(E2EApi api)
    {
        var units = await api.JsonAsync(await api.SendAsync("GET", "/api/org-units"));
        return units.EnumerateArray().Single(u => u.GetProperty("code").GetString() == "S").GetProperty("id").GetGuid();
    }

    /// <summary>The sign-in e-mail of the primary guardian <c>M3Kit.CreateStudentAsync</c> creates for the student of slot 1 (the world's student).</summary>
    public static string GuardianEmail(M2Names names) => names.Email("g1");

    /// <summary>Switches the guardian portal (D30, off by default) on for the unit; call it before the students are created so the invitation is sent.</summary>
    public static async Task SetGuardianPortalAsync(E2EApi api, Guid unit, bool on)
    {
        var response = on
            ? await api.SendAsync("PUT", $"/api/org-units/{unit}/settings/Features.GuardianPortal", new { valueJson = "true", isLocked = false })
            : await api.SendAsync("DELETE", $"/api/org-units/{unit}/settings/Features.GuardianPortal");
        Ok(response.Ok, "setting the guardian portal", response.Status, await response.TextAsync());
    }

    /// <summary>The gradebook of the class course (staff read; the current term unless <paramref name="termId"/> is given).</summary>
    public static async Task<JsonElement> GradebookAsync(E2EApi api, Guid classCourseId, Guid? termId = null)
    {
        var response = await api.SendAsync("GET", $"/api/class-courses/{classCourseId}/gradebook" + (termId is { } t ? $"?termId={t}" : string.Empty));
        Ok(response.Ok, "reading the gradebook", response.Status, await response.TextAsync());
        return await api.JsonAsync(response);
    }

    /// <summary>The id of the item with this English title, read from the gradebook.</summary>
    public static async Task<Guid> ItemIdAsync(E2EApi api, Guid classCourseId, string titleEn)
    {
        var book = await GradebookAsync(api, classCourseId);
        return book.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("titleEn").GetString() == titleEn).GetProperty("id").GetGuid();
    }

    /// <summary>Creates a manual item through the API (the UI path is the item dialog of Task 26); returns its id.</summary>
    public static async Task<Guid> CreateManualItemAsync(E2EApi api, Guid classCourseId, string titleAr, string titleEn, decimal maxPoints)
    {
        var book = await GradebookAsync(api, classCourseId);
        var category = book.GetProperty("categories").EnumerateArray().First().GetProperty("id").GetGuid();
        var response = await api.SendAsync("POST", $"/api/class-courses/{classCourseId}/gradebook/items", new { categoryId = category, titleAr, titleEn, maxPoints });
        Ok(response.Ok, "creating a grade item", response.Status, await response.TextAsync());
        return (await api.JsonAsync(response)).GetProperty("id").GetGuid();
    }

    /// <summary>Publishes (or schedules, with <paramref name="atUtc"/>) items through the API until the publish dialog of Task 27 is on main.</summary>
    public static async Task PublishAsync(E2EApi api, Guid classCourseId, IEnumerable<Guid> itemIds, string mode = "Now", DateTime? atUtc = null)
    {
        var response = await api.SendAsync("POST", $"/api/class-courses/{classCourseId}/gradebook/items/publish",
            new { itemIds = itemIds.ToArray(), mode, atUtc, acknowledgeProvisionalScale = true });
        Ok(response.Ok, "publishing grade items", response.Status, await response.TextAsync());
    }

    /// <summary>Sets a manual score through the API (the UI path is the cell editor, Task 25); the row version comes from a fresh read of the grid.</summary>
    public static async Task SetScoreAsync(E2EApi api, Guid classCourseId, Guid studentId, Guid itemId, decimal points)
    {
        var book = await GradebookAsync(api, classCourseId);
        var version = book.GetProperty("rows").GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("studentId").GetGuid() == studentId).GetProperty("version").GetInt64();
        var response = await api.SendAsync("PUT", $"/api/class-courses/{classCourseId}/gradebook/entries",
            new { studentId, version, changes = new[] { new { itemId, action = "Set", points, reason = (string?)null } } });
        Ok(response.Ok, "saving a score", response.Status, await response.TextAsync());
    }

    private static void Ok(bool ok, string what, int status, string body)
    {
        if (!ok)
        {
            throw new InvalidOperationException($"{what} answered {status}: {body}");
        }
    }
}
