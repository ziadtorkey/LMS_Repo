using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using System.Text.Json;
using Lms.Domain.Notifications;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Lms.Shared.Me;
using Lms.Shared.Notifications;
using Lms.Infrastructure.Notifications;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Notifications;

/// <summary>#147: a suspended client has no valid contexts anywhere, and a fan-out checks entitlement in bulk.</summary>
[Collection(ApiCollection.Name)]
public sealed class SuspendedClientTests(LmsApiFactory factory)
{
    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _assignmentQueries;

        public int AssignmentQueries => _assignmentQueries;

        public void Reset() => Interlocked.Exchange(ref _assignmentQueries, 0);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("RoleAssignments", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _assignmentQueries);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private async Task SuspendAsync(Guid organizationId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        (await db.Organizations.SingleAsync(o => o.Id == organizationId)).SetStatus(OrganizationStatus.Suspended);
        await db.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> GetAsync(string url, Guid user, Guid? assignment)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(TestAuthHandler.UserHeader, user.ToString());
        if (assignment is { } id)
        {
            request.Headers.Add(ContextHeader.Name, id.ToString());
        }

        return await factory.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task Suspended_client_context_is_403_invalid_context_and_absent_from_me()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SC1");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);
        var other = await AccessTestData.SeedAsync(factory, "SC1B");
        var otherAssignment = await AccessTestData.AssignAsync(factory, other, other.School1, BuiltInRoles.Teacher, user);

        var before = (await (await GetAsync("/api/me", user, null)).Content.ReadFromJsonAsync<MeDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
        Assert.Equal(2, before.Contexts.Count);

        await SuspendAsync(tree.Org.Id);

        var denied = await GetAsync("/api/me/permissions", user, assignment.Id);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("invalid_context", await denied.Content.ReadAsStringAsync());

        var me = (await (await GetAsync("/api/me", user, null)).Content.ReadFromJsonAsync<MeDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
        Assert.Equal(new[] { otherAssignment.Id }, me.Contexts.Select(c => c.AssignmentId));
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/me/permissions", user, otherAssignment.Id)).StatusCode);
    }

    [Fact]
    public async Task Fan_out_to_50_recipients_checks_entitlement_with_a_bounded_number_of_queries()
    {
        var counter = new CommandCounter();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.ConfigureDbContext<Lms.Infrastructure.Persistence.LmsDbContext>(o => o.AddInterceptors(counter))));
        var tree = await AccessTestData.SeedAsync(factory, "SC2");
        var users = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();
        foreach (var user in users)
        {
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);
        }

        using var scope = host.Services.CreateScope();
        counter.Reset();
        await scope.ServiceProvider.GetRequiredService<INotificationPublisher>().PublishAsync(new NotificationRequest(
            NotificationEvents.AccessRoleAssigned, tree.Org.Id, users,
            new Dictionary<string, string>
            {
                ["RoleNameAr"] = "معلم", ["RoleNameEn"] = "Teacher", ["OrgUnitNameAr"] = "مدرسة", ["OrgUnitNameEn"] = "School",
            },
            DeepLink: "/me"));

        // One bulk entitlement query for the client, not one per recipient (50).
        Assert.InRange(counter.AssignmentQueries, 1, 3);
    }
}
