using System.Collections.Concurrent;
using System.Net;
using Hangfire;
using Hangfire.Storage;
using Lms.Domain.Abstractions;
using Lms.Shared.Authorization;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Jobs;

/// <summary>Job used by the tests: reports which clients' org units it can see through the normal query filters.</summary>
public sealed class ProbeJob(LmsDbContext db, ICurrentContext context)
{
    public static readonly ConcurrentDictionary<string, (HashSet<Guid> OrganizationIds, Guid? UserId, bool Platform)> Results = new();

    public async Task SeeOrgUnits(string token)
    {
        var organizations = await db.OrgUnits.Select(u => u.OrganizationId).Distinct().ToListAsync();
        Results[token] = (organizations.ToHashSet(), context.UserId, context.IsPlatformAdmin);
    }
}

[Collection(ApiCollection.Name)]
public sealed class BackgroundJobTests(LmsApiFactory factory)
{
    private static async Task<(HashSet<Guid> OrganizationIds, Guid? UserId, bool Platform)> WaitAsync(string token)
    {
        for (var i = 0; i < 300; i++)
        {
            if (ProbeJob.Results.TryGetValue(token, out var result))
            {
                return result;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Job {token} did not run within 30 s.");
    }

    private IBackgroundJobs Jobs(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IBackgroundJobs>();

    [Fact]
    public async Task Enqueued_job_sees_only_its_clients_rows()
    {
        var a = await AccessTestData.SeedAsync(factory, "BJ1");
        var b = await AccessTestData.SeedAsync(factory, "BJ2");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, user);
        var token = Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope();

        Jobs(scope).Enqueue<ProbeJob>(new JobContextSnapshot(user, a.Org.Id, assignment.Id, a.Root.Path, false), j => j.SeeOrgUnits(token));

        var result = await WaitAsync(token);
        Assert.Equal([a.Org.Id], result.OrganizationIds);
        Assert.DoesNotContain(b.Org.Id, result.OrganizationIds);
        Assert.Equal(user, result.UserId);
        Assert.False(result.Platform);
    }

    [Fact]
    public async Task Job_runs_inside_the_subtree_of_the_enqueuing_assignment()
    {
        var a = await AccessTestData.SeedAsync(factory, "BJ3");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        var token = Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope();

        // Scoped to School1: the client's other units are outside the subtree, so only the client's own rows show.
        Jobs(scope).Enqueue<ProbeJob>(new JobContextSnapshot(user, a.Org.Id, assignment.Id, a.School1.Path, false), j => j.SeeOrgUnits(token));

        Assert.Equal([a.Org.Id], (await WaitAsync(token)).OrganizationIds);
    }

    [Fact]
    public async Task Job_whose_assignment_was_deleted_runs_with_an_empty_context()
    {
        var a = await AccessTestData.SeedAsync(factory, "BJ7");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, user);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.RoleAssignments.Remove(await db.RoleAssignments.SingleAsync(x => x.Id == assignment.Id));
            await db.SaveChangesAsync();
        }

        await AssertEmptyAsync(new JobContextSnapshot(user, a.Org.Id, assignment.Id, a.Root.Path, false));
    }

    [Fact]
    public async Task Job_whose_assignment_has_expired_runs_with_an_empty_context()
    {
        var a = await AccessTestData.SeedAsync(factory, "BJ8");
        var user = Guid.NewGuid();
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var assignment = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, user, null, yesterday);

        await AssertEmptyAsync(new JobContextSnapshot(user, a.Org.Id, assignment.Id, a.Root.Path, false));
    }

    [Fact]
    public async Task Job_whose_unit_became_inactive_runs_with_an_empty_context()
    {
        var a = await AccessTestData.SeedAsync(factory, "BJ9");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.Teacher, user);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var unit = await db.OrgUnits.SingleAsync(u => u.Id == a.School1.Id);
            unit.Deactivate();
            await db.SaveChangesAsync();
        }

        await AssertEmptyAsync(new JobContextSnapshot(user, a.Org.Id, assignment.Id, a.School1.Path, false));
    }

    [Fact]
    public async Task Job_with_an_assignment_that_is_not_the_users_runs_with_an_empty_context()
    {
        var a = await AccessTestData.SeedAsync(factory, "BJ10");
        var assignment = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        await AssertEmptyAsync(new JobContextSnapshot(Guid.NewGuid(), a.Org.Id, assignment.Id, a.Root.Path, false));
    }

    private async Task AssertEmptyAsync(JobContextSnapshot snapshot)
    {
        var token = Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope();
        Jobs(scope).Enqueue<ProbeJob>(snapshot, j => j.SeeOrgUnits(token));

        var result = await WaitAsync(token);
        Assert.Empty(result.OrganizationIds);
        Assert.False(result.Platform);
    }

    [Fact]
    public async Task Job_without_a_captured_context_sees_no_scoped_rows()
    {
        await AccessTestData.SeedAsync(factory, "BJ4");
        var token = Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope();

        // Straight through Hangfire, bypassing the facade: nothing is captured.
        scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>().Enqueue<ProbeJob>(j => j.SeeOrgUnits(token));

        var result = await WaitAsync(token);
        Assert.Empty(result.OrganizationIds);
        Assert.Null(result.UserId);
        Assert.False(result.Platform);
    }

    [Fact]
    public async Task System_job_runs_with_the_platform_context_and_sees_every_client()
    {
        var a = await AccessTestData.SeedAsync(factory, "BJ5");
        var b = await AccessTestData.SeedAsync(factory, "BJ6");
        var token = Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope();

        Jobs(scope).EnqueueAsSystem<ProbeJob>(j => j.SeeOrgUnits(token));

        var result = await WaitAsync(token);
        Assert.Contains(a.Org.Id, result.OrganizationIds);
        Assert.Contains(b.Org.Id, result.OrganizationIds);
        Assert.True(result.Platform);
    }

    [Fact]
    public async Task Recurring_job_registration_is_idempotent_and_runs_with_the_platform_context()
    {
        var id = "probe-" + Guid.NewGuid().ToString("N");
        var token = Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope();
        var jobs = Jobs(scope);

        jobs.AddOrUpdateRecurring<ProbeJob>(id, j => j.SeeOrgUnits(token), Cron.Yearly());
        jobs.AddOrUpdateRecurring<ProbeJob>(id, j => j.SeeOrgUnits(token), Cron.Yearly());
        jobs.AddOrUpdateRecurring<ProbeJob>(id, j => j.SeeOrgUnits(token), Cron.Yearly());

        var storage = scope.ServiceProvider.GetRequiredService<JobStorage>();
        using var connection = storage.GetConnection();
        Assert.Single(connection.GetRecurringJobs(), r => r.Id == id);

        scope.ServiceProvider.GetRequiredService<IRecurringJobManager>().Trigger(id);
        Assert.True((await WaitAsync(token)).Platform);
        scope.ServiceProvider.GetRequiredService<IRecurringJobManager>().RemoveIfExists(id);
    }

    [Fact]
    public async Task Dashboard_is_closed_to_anonymous_and_ordinary_users_and_open_to_platform_admins()
    {
        using var client = factory.CreateClient();
        Assert.True((await client.GetAsync("/hangfire")).StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);

        var user = new HttpRequestMessage(HttpMethod.Get, "/hangfire");
        user.Headers.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        Assert.True((await client.SendAsync(user)).StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);

        var admin = new HttpRequestMessage(HttpMethod.Get, "/hangfire/");
        admin.Headers.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        admin.Headers.Add(TestAuthHandler.AdminHeader, "true");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(admin)).StatusCode);
    }
}
