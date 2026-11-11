using System.Text.Json;
using Lms.Domain.Notifications;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Table-level and API-level isolation for every table M1 adds (M1 plan Task 35, PRD §4.2): client A never sees client B,
/// a sibling never sees a sibling, a parent sees its descendants; user-level tables are never visible to another user.
/// Tables still in open PRs (OrgTemplate, NotificationPreference, AcademicYear, Term, CalendarEvent, AuditEntry path
/// visibility) are added here as they merge.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M1IsolationTests(LmsApiFactory factory)
{
    // ---- Notification (Task 10): client-level table (IOptionalTenantOwned) + per-user rows enforced by the endpoints.

    [Fact]
    public async Task Notification_table_client_A_never_sees_client_B_and_no_context_sees_nothing()
    {
        var a = await AccessTestData.SeedAsync(factory, "ISN1A");
        var b = await AccessTestData.SeedAsync(factory, "ISN1B");
        var rowA = await AddNotificationAsync(a.Org.Id, Guid.NewGuid());
        var rowB = await AddNotificationAsync(b.Org.Id, Guid.NewGuid());

        await using (var db = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.Root.Path)))
        {
            var visible = await db.Set<Notification>().Where(n => n.Id == rowA || n.Id == rowB).Select(n => n.Id).ToListAsync();
            Assert.Equal([rowA], visible);
        }

        await using (var db = factory.CreateDbContext(new TestCurrentContext { UserId = Guid.NewGuid() }))
        {
            Assert.False(await db.Set<Notification>().AnyAsync(n => n.Id == rowA || n.Id == rowB));
        }
    }

    [Fact]
    public async Task Notification_api_user_never_sees_or_marks_another_users_rows()
    {
        var a = await AccessTestData.SeedAsync(factory, "ISN2");
        var me = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var colleague = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var mine = await AddNotificationAsync(a.Org.Id, me.UserId);
        var theirs = await AddNotificationAsync(a.Org.Id, colleague.UserId);
        var callers = new CallerClient(factory);

        var ids = await callers.NotificationIdsAsync(me);
        Assert.Contains(mine, ids);
        Assert.DoesNotContain(theirs, ids);

        using var markOther = await callers.SendAsync(me, HttpMethod.Post, $"{Routes.MyNotifications}/{theirs}/read");
        Assert.Equal(404, (int)markOther.StatusCode);
        using var readAll = await callers.SendAsync(me, HttpMethod.Post, Routes.MyNotificationsReadAll);
        Assert.Equal(204, (int)readAll.StatusCode);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Null((await db.Set<Notification>().SingleAsync(n => n.Id == theirs)).ReadAt); // read-all touched only my rows
    }

    [Fact]
    public async Task Notification_api_person_with_two_clients_sees_only_the_active_clients_rows()
    {
        var a = await AccessTestData.SeedAsync(factory, "ISN3A");
        var b = await AccessTestData.SeedAsync(factory, "ISN3B");
        var person = Guid.NewGuid();
        var inA = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, person);
        var inB = await AccessTestData.AssignAsync(factory, b, b.School1, BuiltInRoles.Teacher, person);
        var rowA = await AddNotificationAsync(a.Org.Id, person);
        var rowB = await AddNotificationAsync(b.Org.Id, person);
        var callers = new CallerClient(factory);

        var asA = await callers.NotificationIdsAsync(inA);
        var asB = await callers.NotificationIdsAsync(inB);

        Assert.Contains(rowA, asA);
        Assert.DoesNotContain(rowB, asA);
        Assert.Contains(rowB, asB);
        Assert.DoesNotContain(rowA, asB);
    }

    private async Task<Guid> AddNotificationAsync(Guid organizationId, Guid userId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var row = new Notification(organizationId, userId, "Access.RoleAssigned", "{}", null, DateTime.UtcNow);
        db.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    /// <summary>Sends as the user of a role assignment, in that assignment's context.</summary>
    private sealed class CallerClient(LmsApiFactory factory)
    {
        public Task<HttpResponseMessage> SendAsync(Lms.Domain.Access.RoleAssignment who, HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Add(TestAuthHandler.UserHeader, who.UserId.ToString());
            request.Headers.Add(ContextHeader.Name, who.Id.ToString());
            return factory.CreateClient().SendAsync(request);
        }

        public async Task<List<Guid>> NotificationIdsAsync(Lms.Domain.Access.RoleAssignment who)
        {
            using var response = await SendAsync(who, HttpMethod.Get, $"{Routes.MyNotifications}?pageSize=200");
            Assert.Equal(200, (int)response.StatusCode);
            var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            return page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        }
    }
}
