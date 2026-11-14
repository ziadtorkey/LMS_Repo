using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.Domain.Configuration;
using Lms.Domain.Notifications;
using Lms.Domain.People;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Guardians;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.People;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Students;

/// <summary>A client with one student who has a login, for the self-edit and change-request tests (M3 Task 22, #230).</summary>
internal sealed class SelfServiceKit(LmsApiFactory factory, WebApplicationFactory<Program> host) : IDisposable
{
    public void Dispose() => Kit.Dispose();

    public sealed record World(AccessTree Tree, RoleAssignment Admin, StudentProfile Student, RoleAssignment Me);

    public GuardianTestKit Kit { get; } = new(factory, host);

    public async Task<World> SetUpAsync(string name)
    {
        var (tree, admin) = await Kit.SetUpAsync(name);
        var student = await Kit.StudentAsync(tree.School1);
        var userId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var stored = await db.Set<StudentProfile>().IgnoreQueryFilters().SingleAsync(s => s.Id == student.Id);
            stored.LinkUser(userId);
            await db.SaveChangesAsync();
        }

        var me = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Student, userId);
        return new World(tree, admin, student, me);
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, RoleAssignment who, object? body = null) =>
        Kit.SendAsync(method, url, who, body);

    public static Dictionary<string, string?> Changes(params (string Field, string? Value)[] items) =>
        items.ToDictionary(i => i.Field, i => i.Value);

    public async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(GuardianTestKit.Web))!;

    public async Task<string> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString()!;

    /// <summary>Admin moves fields out of (or into) the critical set at the school (the Students.CriticalFields setting).</summary>
    public async Task SetCriticalAsync(AccessTree tree, params string[] fields)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new OrgUnitSetting(
            tree.Org.Id, tree.School1.Id, tree.School1.Path, SettingKeys.StudentsCriticalFields.Name,
            System.Text.Json.JsonSerializer.Serialize(fields), false));
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<Notification>> NotificationsOfAsync(Guid userId, string eventKey)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Notification>().Where(n => n.UserId == userId && n.EventKey == eventKey).ToListAsync();
    }

    public async Task<StudentProfile> ReloadAsync(Guid studentId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<StudentProfile>().IgnoreQueryFilters().SingleAsync(s => s.Id == studentId);
    }
}
