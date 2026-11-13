using System.Collections.Concurrent;
using Hangfire;
using Hangfire.Storage;
using Lms.Domain.Abstractions;
using Lms.Infrastructure.Jobs;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Jobs;

/// <summary>Job used by the scheduling tests: records every run (when, with which value, and as whom).</summary>
public sealed class ScheduleProbeJob(ICurrentContext context)
{
    public static readonly ConcurrentDictionary<string, ConcurrentQueue<(DateTime At, string Value, Guid? UserId, bool Platform)>> Runs = new();

    public Task RunAsync(string token, string value)
    {
        Runs.GetOrAdd(token, _ => new()).Enqueue((DateTime.UtcNow, value, context.UserId, context.IsPlatformAdmin));
        return Task.CompletedTask;
    }
}

/// <summary>Delayed and scheduled enqueue with idempotent keys: one key is one pending job, however often it is scheduled.</summary>
[Collection(ApiCollection.Name)]
public sealed class ScheduledJobTests(LmsApiFactory factory) : IAsyncLifetime
{
    private static readonly SemaphoreSlim Warm = new(1, 1);
    private static bool _warmed;

    /// <summary>
    /// A freshly started Hangfire server picks up its first delayed job late (observed 35 to 60 s after the due time, never lost). The
    /// tests wait for one throw-away delayed job once, so they measure the scheduling and not the server start.
    /// </summary>
    public async Task InitializeAsync()
    {
        await Warm.WaitAsync();
        try
        {
            if (_warmed)
            {
                return;
            }

            var token = NewToken();
            using var scope = factory.Services.CreateScope();
            Jobs(scope).ScheduleAsSystem<ScheduleProbeJob>($"sj:{token}", DateTimeOffset.UtcNow.AddSeconds(1), j => j.RunAsync(token, "warm"));
            await WaitForRunsAsync(token, 1, seconds: 150);
            _warmed = true;
        }
        finally
        {
            Warm.Release();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string NewToken() => Guid.NewGuid().ToString("N");

    private static List<(DateTime At, string Value, Guid? UserId, bool Platform)> RunsOf(string token) =>
        ScheduleProbeJob.Runs.TryGetValue(token, out var queue) ? [.. queue] : [];

    private static async Task<List<(DateTime At, string Value, Guid? UserId, bool Platform)>> WaitForRunsAsync(string token, int count, int seconds = 100)
    {
        for (var i = 0; i < seconds * 10; i++)
        {
            if (RunsOf(token).Count >= count)
            {
                return RunsOf(token);
            }

            await Task.Delay(100);
        }

        var states = string.Join(", ", JobsOfToken(token).Select(id => id + "=" + StateOf(id) + "/" + (DetailOf(id) ?? "-")));
        throw new TimeoutException($"Job {token} ran {RunsOf(token).Count} time(s), expected {count}. Jobs: {states}");
    }

    private static IEnumerable<string> JobsOfToken(string token)
    {
        using var connection = JobStorage.Current.GetConnection();
        var mapping = connection.GetAllEntriesFromHash(ScheduledJobKeys.HashKey("sj:" + token));
        return mapping is not null && mapping.TryGetValue("jobId", out var id) ? [id] : [];
    }

    private static string? DetailOf(string jobId)
    {
        using var connection = JobStorage.Current.GetConnection();
        var data = connection.GetStateData(jobId);
        return data is null ? null : data.Name + ":" + string.Join(";", data.Data.Select(kv => kv.Key + "=" + kv.Value));
    }

    private static string? StateOf(string jobId)
    {
        using var connection = JobStorage.Current.GetConnection();
        return connection.GetJobData(jobId)?.State;
    }

    private IBackgroundJobs Jobs(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IBackgroundJobs>();

    [Fact]
    public async Task Scheduled_job_runs_once_not_before_its_time_with_the_given_context()
    {
        var token = NewToken();
        var a = await AccessTestData.SeedAsync(factory, "SJ1");
        var user = Guid.NewGuid();
        var assignment = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, user);
        using var scope = factory.Services.CreateScope();
        var due = DateTimeOffset.UtcNow.AddSeconds(5);

        var jobId = Jobs(scope).Schedule<ScheduleProbeJob>($"sj:{token}", due, new JobContextSnapshot(user, a.Org.Id, assignment.Id, a.Root.Path, false),
            j => j.RunAsync(token, "v1"));

        // The contract of our code, independent of when a server happens to pick the job up: stored as Scheduled for exactly the due time.
        Assert.Equal("Scheduled", StateOf(jobId));
        using (var connection = JobStorage.Current.GetConnection())
        {
            var enqueueAt = Hangfire.Common.JobHelper.DeserializeDateTime(connection.GetStateData(jobId)!.Data["EnqueueAt"]);
            Assert.InRange((enqueueAt - due.UtcDateTime).TotalSeconds, -1, 1);
        }

        Assert.Empty(RunsOf(token)); // scheduled, not run now
        var runs = await WaitForRunsAsync(token, 1);
        await Task.Delay(3000); // and not twice
        Assert.Single(RunsOf(token));
        Assert.True(runs[0].At >= due.UtcDateTime.AddSeconds(-1.5), $"ran before its time: due {due.UtcDateTime:O}, ran {runs[0].At:O}, job {jobId}");
        Assert.Equal((user, false, "v1"), (runs[0].UserId, runs[0].Platform, runs[0].Value));
    }

    [Fact]
    public async Task Scheduling_the_same_key_again_moves_the_pending_job_and_it_runs_once_at_the_new_time()
    {
        var token = NewToken();
        var key = $"sj:{token}";
        using var scope = factory.Services.CreateScope();
        var jobs = Jobs(scope);

        var first = jobs.ScheduleAsSystem<ScheduleProbeJob>(key, DateTimeOffset.UtcNow.AddHours(1), j => j.RunAsync(token, "same"));
        var second = jobs.ScheduleAsSystem<ScheduleProbeJob>(key, DateTimeOffset.UtcNow.AddHours(2), j => j.RunAsync(token, "same"));
        var third = jobs.ScheduleAsSystem<ScheduleProbeJob>(key, DateTimeOffset.UtcNow.AddSeconds(2), j => j.RunAsync(token, "same"));

        Assert.Equal(first, second); // the same Hangfire job was moved, not duplicated
        Assert.Equal(first, third);
        await WaitForRunsAsync(token, 1);
        await Task.Delay(3000);
        Assert.Single(RunsOf(token));
    }

    [Fact]
    public async Task Same_key_with_other_arguments_or_context_replaces_the_pending_job()
    {
        var token = NewToken();
        var key = $"sj:{token}";
        using var scope = factory.Services.CreateScope();
        var jobs = Jobs(scope);

        var old = jobs.ScheduleAsSystem<ScheduleProbeJob>(key, DateTimeOffset.UtcNow.AddHours(1), j => j.RunAsync(token, "old"));
        var replaced = jobs.ScheduleAsSystem<ScheduleProbeJob>(key, DateTimeOffset.UtcNow.AddSeconds(2), j => j.RunAsync(token, "new"));

        Assert.NotEqual(old, replaced);
        Assert.Equal("Deleted", StateOf(old)); // the old pending job is gone, only the replacement is waiting
        Assert.Equal("Scheduled", StateOf(replaced));
        var runs = await WaitForRunsAsync(token, 1);
        await Task.Delay(3000);
        Assert.Equal(["new"], RunsOf(token).Select(r => r.Value).ToArray()); // only the replacement ran
        Assert.Single(runs);
    }

    [Fact]
    public async Task Parallel_schedules_of_one_key_leave_exactly_one_pending_job()
    {
        var token = NewToken();
        var key = $"sj:{token}";
        using var scope = factory.Services.CreateScope();

        var ids = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(() =>
        {
            using var inner = factory.Services.CreateScope();
            return Jobs(inner).ScheduleAsSystem<ScheduleProbeJob>(key, DateTimeOffset.UtcNow.AddHours(1).AddMinutes(i), j => j.RunAsync(token, "p"));
        })));

        Assert.True(ids.Distinct().Count() == 1, string.Join(", ", ids.Distinct().Select(i => i + "=" + StateOf(i)))); // ten callers, one job
        Assert.Equal("Scheduled", StateOf(ids[0]));
        Assert.True(Jobs(scope).Cancel(key));
        Assert.Equal("Deleted", StateOf(ids[0]));
    }

    [Fact]
    public async Task Cancel_removes_the_pending_job_and_reports_false_when_there_is_none()
    {
        var token = NewToken();
        var key = $"sj:{token}";
        using var scope = factory.Services.CreateScope();
        var jobs = Jobs(scope);
        Assert.False(jobs.Cancel(key)); // never scheduled

        // Due far enough away that a slow runner (a starved connection pool can stall one call for seconds) cannot reach it before the cancel.
        var jobId = jobs.ScheduleAsSystem<ScheduleProbeJob>(key, DateTimeOffset.UtcNow.AddSeconds(20), j => j.RunAsync(token, "x"));
        var stateBefore = StateOf(jobId);
        var cancelled = jobs.Cancel(key);
        var again = jobs.Cancel(key);
        await Task.Delay(1000);

        Assert.True(cancelled, $"job {jobId} was {stateBefore} before the cancel, {StateOf(jobId)} after; runs: {RunsOf(token).Count}");
        Assert.False(again);
        Assert.Empty(RunsOf(token)); // it never ran
    }

    [Fact]
    public async Task After_the_job_ran_the_key_is_free_and_a_new_schedule_is_a_new_run()
    {
        var token = NewToken();
        var key = $"sj:{token}";
        using var scope = factory.Services.CreateScope();
        var jobs = Jobs(scope);

        var first = jobs.ScheduleAsSystem<ScheduleProbeJob>(key, DateTimeOffset.UtcNow.AddSeconds(1), j => j.RunAsync(token, "again"));
        await WaitForRunsAsync(token, 1);
        await Task.Delay(1500); // let it finish
        var second = jobs.ScheduleAsSystem<ScheduleProbeJob>(key, DateTimeOffset.UtcNow.AddSeconds(1), j => j.RunAsync(token, "again"));

        Assert.NotEqual(first, second);
        await WaitForRunsAsync(token, 2);
        Assert.False(jobs.Cancel(key)); // already ran
    }

    [Fact]
    public async Task The_delay_overload_and_the_system_context_work()
    {
        var token = NewToken();
        using var scope = factory.Services.CreateScope();

        Jobs(scope).Schedule<ScheduleProbeJob>($"sj:{token}", TimeSpan.FromSeconds(2), j => j.RunAsync(token, "delay"));
        var systemToken = NewToken();
        Jobs(scope).ScheduleAsSystem<ScheduleProbeJob>($"sj:{systemToken}", DateTimeOffset.UtcNow.AddSeconds(2), j => j.RunAsync(systemToken, "sys"));

        var run = (await WaitForRunsAsync(token, 1))[0];
        var system = (await WaitForRunsAsync(systemToken, 1))[0];
        Assert.False(run.Platform); // the caller here has no platform context
        Assert.True(system.Platform);
    }

    [Theory]
    [InlineData("")]
    [InlineData("with space")]
    [InlineData("quote'")]
    public void An_invalid_key_is_refused_before_anything_is_stored(string key)
    {
        using var scope = factory.Services.CreateScope();

        Assert.Throws<ArgumentException>(() => Jobs(scope).ScheduleAsSystem<ScheduleProbeJob>(key, DateTimeOffset.UtcNow.AddHours(1), j => j.RunAsync("t", "v")));
        Assert.Throws<ArgumentException>(() => Jobs(scope).Cancel(key));
    }

    private static TimeSpan? MappingTtlOf(string key)
    {
        using var connection = JobStorage.Current.GetConnection();
        return connection is JobStorageConnection storage ? storage.GetHashTtl(ScheduledJobKeys.HashKey(key)) : null;
    }

    [Fact]
    public void The_key_mapping_expires_so_keys_never_pile_up_and_a_reschedule_moves_the_expiry()
    {
        var token = NewToken();
        var key = $"sj:{token}";
        using var scope = factory.Services.CreateScope();
        var jobs = Jobs(scope);

        jobs.ScheduleAsSystem<ScheduleProbeJob>(key, DateTimeOffset.UtcNow.AddHours(1), j => j.RunAsync(token, "ttl"));
        var first = MappingTtlOf(key);
        jobs.ScheduleAsSystem<ScheduleProbeJob>(key, DateTimeOffset.UtcNow.AddHours(5), j => j.RunAsync(token, "ttl"));
        var second = MappingTtlOf(key);
        jobs.Cancel(key);

        Assert.NotNull(first);
        Assert.InRange(first!.Value, TimeSpan.FromHours(1) + ScheduledJobKeys.MappingGrace - TimeSpan.FromMinutes(2), TimeSpan.FromHours(1) + ScheduledJobKeys.MappingGrace);
        Assert.InRange(second!.Value, TimeSpan.FromHours(5) + ScheduledJobKeys.MappingGrace - TimeSpan.FromMinutes(2), TimeSpan.FromHours(5) + ScheduledJobKeys.MappingGrace);
        var none = MappingTtlOf("sj:never-scheduled-" + token);
        Assert.True(none is null || none < TimeSpan.Zero); // no mapping, no expiry
    }
}
