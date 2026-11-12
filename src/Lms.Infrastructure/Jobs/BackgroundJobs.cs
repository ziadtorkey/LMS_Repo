using System.Linq.Expressions;
using System.Text.Json;
using Hangfire;
using Hangfire.Client;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Lms.Domain.Abstractions;
using MySqlConnector;

namespace Lms.Infrastructure.Jobs;

/// <summary>
/// Enqueues background jobs that carry a caller context. A job enqueued through <see cref="IBackgroundJobClient"/>
/// directly has no captured context and therefore sees no scoped rows; use this facade instead.
/// </summary>
public interface IBackgroundJobs
{
    /// <summary>Enqueues the job with the context of the current caller (client, role assignment, subtree).</summary>
    string Enqueue<T>(Expression<Func<T, Task>> job);

    /// <summary>Enqueues the job with an explicit context (e.g. one captured earlier).</summary>
    string Enqueue<T>(JobContextSnapshot context, Expression<Func<T, Task>> job);

    /// <summary>Enqueues the job with the platform context: it sees every client. For system work only.</summary>
    string EnqueueAsSystem<T>(Expression<Func<T, Task>> job);

    /// <summary>
    /// Registers (or updates in place) a recurring system job. Safe to call on every start: one id is one schedule.
    /// Recurring jobs always run with the platform context; the cron is read in the <c>Jobs:TimeZone</c> zone.
    /// </summary>
    void AddOrUpdateRecurring<T>(string recurringJobId, Expression<Func<T, Task>> job, string cronExpression);

    /// <summary>
    /// Runs the job once at <paramref name="runAt"/> with the context of the current caller. The <paramref name="key"/> makes it idempotent:
    /// it identifies one pending job, so scheduling the same key again moves that job (same method, arguments and context) or replaces it
    /// (anything else) and never leaves two pending jobs. Once the job has started or finished, the key is free: a new schedule is a new run.
    /// Use ids in the key (for example <c>live-reminder:{sessionId}</c>); a key is global, not per client. Returns the Hangfire job id.
    /// </summary>
    /// <exception cref="ArgumentException">The key is not 1 to 200 characters of letters, digits and <c>- _ . : /</c>.</exception>
    string Schedule<T>(string key, DateTimeOffset runAt, Expression<Func<T, Task>> job);

    /// <summary>The same, <paramref name="delay"/> from now (a negative delay runs as soon as possible).</summary>
    string Schedule<T>(string key, TimeSpan delay, Expression<Func<T, Task>> job);

    /// <summary>The same with an explicit context (for example one captured earlier).</summary>
    string Schedule<T>(string key, DateTimeOffset runAt, JobContextSnapshot context, Expression<Func<T, Task>> job);

    /// <summary>The same with the platform context: it sees every client. For system work only.</summary>
    string ScheduleAsSystem<T>(string key, DateTimeOffset runAt, Expression<Func<T, Task>> job);

    /// <summary>Deletes the pending job of the key; false when there is none (never scheduled, already started or finished).</summary>
    bool Cancel(string key);
}

public sealed class BackgroundJobs(
    IBackgroundJobClient client, IRecurringJobManager recurring, ICurrentContext current, JobSettings settings, JobStorage storage,
    IClock clock, IJobKeyLock keyLock) : IBackgroundJobs
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MappingGrace = TimeSpan.FromDays(30);

    public string Enqueue<T>(Expression<Func<T, Task>> job) => Enqueue(JobContextSnapshot.From(current), job);

    public string Enqueue<T>(JobContextSnapshot context, Expression<Func<T, Task>> job)
    {
        using (CaptureJobContextFilter.Begin(context))
        {
            return client.Enqueue(job);
        }
    }

    public string EnqueueAsSystem<T>(Expression<Func<T, Task>> job) => Enqueue(JobContextSnapshot.System, job);

    public void AddOrUpdateRecurring<T>(string recurringJobId, Expression<Func<T, Task>> job, string cronExpression) =>
        recurring.AddOrUpdate(recurringJobId, job, cronExpression, new RecurringJobOptions { TimeZone = settings.TimeZone });

    public string Schedule<T>(string key, DateTimeOffset runAt, Expression<Func<T, Task>> job) =>
        Schedule(key, runAt, JobContextSnapshot.From(current), job);

    public string Schedule<T>(string key, TimeSpan delay, Expression<Func<T, Task>> job) => Schedule(key, Now() + delay, job);

    public string ScheduleAsSystem<T>(string key, DateTimeOffset runAt, Expression<Func<T, Task>> job) =>
        Schedule(key, runAt, JobContextSnapshot.System, job);

    public string Schedule<T>(string key, DateTimeOffset runAt, JobContextSnapshot context, Expression<Func<T, Task>> job)
    {
        ScheduledJobKeys.Validate(key);
        var fingerprint = ScheduledJobKeys.Fingerprint(
            InvocationData.SerializeJob(Job.FromExpression(job)).SerializePayload(), JsonSerializer.Serialize(context));
        using var connection = storage.GetConnection();
        // One schedule per key at a time: two callers racing on one key (a reschedule and a retry) cannot both create a job.
        using var gate = keyLock.Acquire(ScheduledJobKeys.LockKey(key), LockTimeout);
        var mapping = connection.GetAllEntriesFromHash(ScheduledJobKeys.HashKey(key));
        var existingId = mapping is not null && mapping.TryGetValue("jobId", out var id) ? id : null;
        var state = existingId is null ? null : connection.GetJobData(existingId)?.State;
        var action = ScheduledJobKeys.Decide(state, mapping is not null && mapping.GetValueOrDefault("fingerprint") == fingerprint);
        if (action == ScheduleAction.Reschedule && client.ChangeState(existingId!, new ScheduledState(runAt.UtcDateTime), ScheduledJobKeys.ScheduledState))
        {
            Remember(connection, key, existingId!, fingerprint, runAt);
            return existingId!;
        }

        // Replace (other arguments or context), or a reschedule that lost a race with the job leaving Scheduled: a fresh job.
        if (action != ScheduleAction.Create && existingId is not null)
        {
            client.ChangeState(existingId, new DeletedState(), ScheduledJobKeys.ScheduledState);
        }

        string jobId;
        using (CaptureJobContextFilter.Begin(context))
        {
            jobId = client.Schedule(job, runAt);
        }

        Remember(connection, key, jobId, fingerprint, runAt);
        return jobId;
    }

    public bool Cancel(string key)
    {
        ScheduledJobKeys.Validate(key);
        using var connection = storage.GetConnection();
        using var gate = keyLock.Acquire(ScheduledJobKeys.LockKey(key), LockTimeout);
        var mapping = connection.GetAllEntriesFromHash(ScheduledJobKeys.HashKey(key));
        if (mapping is null || !mapping.TryGetValue("jobId", out var jobId))
        {
            return false;
        }

        var deleted = connection.GetJobData(jobId)?.State == ScheduledJobKeys.ScheduledState
                      && client.ChangeState(jobId, new DeletedState(), ScheduledJobKeys.ScheduledState);
        using var transaction = connection.CreateWriteTransaction();
        transaction.RemoveHash(ScheduledJobKeys.HashKey(key));
        transaction.Commit();
        return deleted;
    }

    private DateTimeOffset Now() => new(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc));

    private void Remember(IStorageConnection connection, string key, string jobId, string fingerprint, DateTimeOffset runAt) =>
        Retrying(() =>
        {
            RememberOnce(connection, key, jobId, fingerprint, runAt);
            return true;
        });

    /// <summary>MySQL picks one of two concurrent lock or write transactions as a deadlock victim: the loser simply tries again.</summary>
    private static T Retrying<T>(Func<T> work)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return work();
            }
            catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.LockDeadlock && attempt < 6)
            {
                Thread.Sleep(Random.Shared.Next(10, 40) * attempt);
            }
        }
    }

    private void RememberOnce(IStorageConnection connection, string key, string jobId, string fingerprint, DateTimeOffset runAt)
    {
        using var transaction = connection.CreateWriteTransaction();
        var hashKey = ScheduledJobKeys.HashKey(key);
        transaction.SetRangeInHash(hashKey, [new("jobId", jobId), new("fingerprint", fingerprint)]);
        // The mapping outlives the run by a grace period, then goes by itself (the job state, not the mapping, decides what is pending).
        var wait = runAt - Now();
        if (transaction is JobStorageTransaction expiring)
        {
            expiring.ExpireHash(hashKey, (wait > TimeSpan.Zero ? wait : TimeSpan.Zero) + MappingGrace);
        }

        transaction.Commit();
    }
}

/// <summary>
/// Client filter: stores the context snapshot of the enqueuing caller as a job parameter. Jobs created by the recurring
/// scheduler (no enqueuing caller) get the platform context.
/// </summary>
public sealed class CaptureJobContextFilter : IClientFilter
{
    private static readonly AsyncLocal<JobContextSnapshot?> Pending = new();

    /// <summary>Makes <paramref name="context"/> the one captured by jobs created until the scope is disposed.</summary>
    public static IDisposable Begin(JobContextSnapshot context)
    {
        var previous = Pending.Value;
        Pending.Value = context;
        return new Restore(previous);
    }

    public void OnCreating(CreatingContext filterContext)
    {
        var snapshot = Pending.Value;
        if (snapshot is null && filterContext.Parameters.ContainsKey("RecurringJobId"))
        {
            snapshot = JobContextSnapshot.System;
        }

        if (snapshot is not null)
        {
            filterContext.SetJobParameter(JobContextSnapshot.ParameterName, JsonSerializer.Serialize(snapshot));
        }
    }

    public void OnCreated(CreatedContext filterContext)
    {
    }

    private sealed class Restore(JobContextSnapshot? previous) : IDisposable
    {
        public void Dispose() => Pending.Value = previous;
    }
}
