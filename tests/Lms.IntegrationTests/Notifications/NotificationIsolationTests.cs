using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Notifications;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Notifications;

[Collection(ApiCollection.Name)]
public sealed class NotificationIsolationTests(LmsApiFactory factory)
{
    private async Task AddRowAsync(Guid? organizationId, Guid userId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new Notification(organizationId, userId, NotificationEvents.AccessRoleAssigned,
            "{\"RoleNameAr\":\"x\",\"RoleNameEn\":\"x\",\"OrgUnitNameAr\":\"x\",\"OrgUnitNameEn\":\"x\"}", null, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Notifications_of_client_B_are_invisible_in_client_A_context()
    {
        var a = await AccessTestData.SeedAsync(factory, "NI1A");
        var b = await AccessTestData.SeedAsync(factory, "NI1B");
        var user = Guid.NewGuid(); // one person with a role in both clients
        var inA = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        var inB = await AccessTestData.AssignAsync(factory, b, b.School1, BuiltInRoles.Teacher, user);
        await AddRowAsync(a.Org.Id, user);
        await AddRowAsync(b.Org.Id, user);
        await AddRowAsync(b.Org.Id, user);

        async Task<NotificationPageDto> PageAsync(Guid assignment)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, Routes.MyNotifications);
            request.Headers.Add(TestAuthHandler.UserHeader, user.ToString());
            request.Headers.Add(ContextHeader.Name, assignment.ToString());
            var response = await factory.CreateClient().SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<NotificationPageDto>())!;
        }

        Assert.Equal(1, (await PageAsync(inA.Id)).Total);
        Assert.Equal(2, (await PageAsync(inB.Id)).Total);
    }

    [Fact]
    public async Task Table_filter_hides_other_clients_and_platform_rows_from_a_client_context()
    {
        var a = await AccessTestData.SeedAsync(factory, "NI2A");
        var b = await AccessTestData.SeedAsync(factory, "NI2B");
        var user = Guid.NewGuid();
        await AddRowAsync(a.Org.Id, user);
        await AddRowAsync(b.Org.Id, user);
        await AddRowAsync(null, user);

        await using var asA = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.Root.Path));
        await using var noClient = factory.CreateDbContext(new TestCurrentContext { UserId = user });
        await using var admin = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());

        Assert.Equal([a.Org.Id], await asA.Set<Notification>().Where(x => x.UserId == user).Select(x => x.OrganizationId!.Value).ToListAsync());
        Assert.Empty(await noClient.Set<Notification>().ToListAsync());
        Assert.Equal(3, await admin.Set<Notification>().CountAsync(x => x.UserId == user));
    }

    [Fact]
    public async Task User_without_an_active_context_sees_no_notifications()
    {
        var a = await AccessTestData.SeedAsync(factory, "NI3");
        var user = Guid.NewGuid();
        await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        await AddRowAsync(a.Org.Id, user);
        var request = new HttpRequestMessage(HttpMethod.Get, Routes.MyNotifications);
        request.Headers.Add(TestAuthHandler.UserHeader, user.ToString());

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await response.Content.ReadFromJsonAsync<NotificationPageDto>())!.Total);
    }
}
