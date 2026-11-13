using System.Net;
using Hangfire;
using Hangfire.Storage;
using Lms.Domain.People;
using Lms.Infrastructure.People.Staff;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared.Authorization;
using Lms.Api.Features.Jobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Jobs;

[Collection(ApiCollection.Name)]
public sealed class DevJobTriggerTests(LmsApiFactory factory)
{
    private static string Url(string jobId, string? asOf = null) =>
        $"/api/dev/jobs/{jobId}/run" + (asOf is null ? string.Empty : $"?asOf={asOf}");

    private static HttpRequestMessage Post(string url, bool admin, Guid? user = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (user is not null || admin)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, (user ?? Guid.NewGuid()).ToString());
        }

        if (admin)
        {
            request.Headers.Add(TestAuthHandler.AdminHeader, "true");
        }

        return request;
    }

    [Fact]
    public async Task Platform_admin_runs_the_job_now_for_the_given_day_and_waits_for_it()
    {
        var tree = await AccessTestData.SeedAsync(factory, "DJ1");
        var staff = await PeopleTestKit.AddStaffAsync(factory, tree.School1, expiresOn: new DateOnly(2040, 6, 14));
        var untouched = await PeopleTestKit.AddStaffAsync(factory, tree.School1, expiresOn: new DateOnly(2040, 6, 15));

        var response = await factory.CreateClient().SendAsync(Post(Url(StaffExpiryJob.RecurringJobId, "2040-06-15"), admin: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        // The response came back only after the job finished: the effect is already there.
        Assert.Equal(PersonStatus.Expired, (await db.Set<StaffProfile>().SingleAsync(p => p.Id == staff.Id)).Status);
        Assert.Equal(PersonStatus.Active, (await db.Set<StaffProfile>().SingleAsync(p => p.Id == untouched.Id)).Status);
    }

    [Fact]
    public async Task Unknown_job_is_404_and_a_bad_day_is_400()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Post(Url("no-such-job"), admin: true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.SendAsync(Post(Url(StaffExpiryJob.RecurringJobId, "15/06/2040"), admin: true))).StatusCode);
    }

    [Fact]
    public async Task Client_admins_are_403_and_anonymous_is_401()
    {
        var tree = await AccessTestData.SeedAsync(factory, "DJ3");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, Lms.Shared.Authorization.BuiltInRoles.OrgAdmin, Guid.NewGuid());
        using var client = factory.CreateClient();
        var asClientAdmin = Post(Url(StaffExpiryJob.RecurringJobId), admin: false, user: admin.UserId);
        asClientAdmin.Headers.Add(ContextHeader.Name, admin.Id.ToString());

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(asClientAdmin)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.SendAsync(Post(Url(StaffExpiryJob.RecurringJobId), admin: false))).StatusCode);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Route_is_not_mapped_outside_Development(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Services.AddRouting();
        builder.Services.AddAuthorization();
        using var app = builder.Build();

        new JobsModule().MapEndpoints(app);

        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText).ToList();
        Assert.DoesNotContain(JobsModule.DevRunPattern, routes); // the Development case is the 200 test above
    }

    [Fact]
    public void Jobs_TimeZone_is_applied_to_the_recurring_schedules()
    {
        using var riyadh = factory.WithWebHostBuilder(b => b.UseSetting("Jobs:ServerEnabled", "true").UseSetting("Jobs:TimeZone", "Asia/Riyadh")); // the server flag registers the recurring jobs
        using var scope = riyadh.Services.CreateScope(); // starting the host registers the recurring jobs
        using var connection = scope.ServiceProvider.GetRequiredService<JobStorage>().GetConnection();

        var job = Assert.Single(connection.GetRecurringJobs(), r => r.Id == StaffExpiryJob.RecurringJobId);

        Assert.Equal("Asia/Riyadh", job.TimeZoneId);
        Assert.Equal("0 2 * * *", job.Cron);

        // Put the shared storage back to the default zone for the other tests.
        using var utc = factory.WithWebHostBuilder(b => b.UseSetting("Jobs:ServerEnabled", "true"));
        using var utcScope = utc.Services.CreateScope();
        using var again = utcScope.ServiceProvider.GetRequiredService<JobStorage>().GetConnection();
        Assert.Equal("UTC", Assert.Single(again.GetRecurringJobs(), r => r.Id == StaffExpiryJob.RecurringJobId).TimeZoneId);
    }
}
