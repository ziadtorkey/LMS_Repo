using System.Linq.Expressions;
using System.Text.Json;
using Hangfire;
using Hangfire.Client;
using Lms.Domain.Abstractions;

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
    /// Recurring jobs always run with the platform context.
    /// </summary>
    void AddOrUpdateRecurring<T>(string recurringJobId, Expression<Func<T, Task>> job, string cronExpression);
}

public sealed class BackgroundJobs(
    IBackgroundJobClient client, IRecurringJobManager recurring, ICurrentContext current) : IBackgroundJobs
{
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
        recurring.AddOrUpdate(recurringJobId, job, cronExpression);
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
