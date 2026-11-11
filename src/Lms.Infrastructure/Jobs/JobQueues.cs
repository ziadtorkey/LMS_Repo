namespace Lms.Infrastructure.Jobs;

/// <summary>
/// Hangfire queue names. A job on a named queue (<c>[Queue(JobQueues.Imports)]</c>) is picked up only by a server that
/// lists the queue, so every queue used by a job must be in <see cref="All"/>, which the Hangfire server is started with.
/// </summary>
public static class JobQueues
{
    public const string Default = "default";
    public const string Imports = "imports";

    public static readonly string[] All = [Default, Imports];
}
