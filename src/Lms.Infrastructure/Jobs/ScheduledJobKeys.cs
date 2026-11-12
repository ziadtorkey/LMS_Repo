using System.Security.Cryptography;
using System.Text;

namespace Lms.Infrastructure.Jobs;

/// <summary>What <c>Schedule</c> does with the job already registered under a key.</summary>
public enum ScheduleAction
{
    /// <summary>No pending job under the key: create one.</summary>
    Create,

    /// <summary>The same pending job: move it to the new time (it still runs once).</summary>
    Reschedule,

    /// <summary>A pending job with other arguments or another context: delete it and create the new one.</summary>
    Replace,
}

/// <summary>
/// The pure rules of idempotent scheduling: key shape, the names the key maps to in the job storage, the fingerprint of a job, and the
/// decision. A key identifies one pending job: scheduling it again never leaves two pending jobs, so a reschedule cannot double-run.
/// </summary>
public static class ScheduledJobKeys
{
    public const int MaxKeyLength = 200;
    public const string ScheduledState = "Scheduled";

    /// <summary>Hangfire hash that remembers which job a key currently points at.</summary>
    public static string HashKey(string key) => "lms:scheduled:" + key;

    /// <summary>The distributed lock that serialises concurrent schedules of one key.</summary>
    public static string LockKey(string key) => "lms:scheduled-lock:" + key;

    /// <exception cref="ArgumentException">The key is empty, too long or has a character outside letters, digits and <c>- _ . : /</c>.</exception>
    public static void Validate(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength || !key.All(IsAllowed))
        {
            throw new ArgumentException(
                $"A job key is 1 to {MaxKeyLength} characters of letters, digits and - _ . : / (got '{key}').", nameof(key));
        }
    }

    private static bool IsAllowed(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':' or '/';

    /// <summary>Identifies what the job does (method and arguments) and who it runs as; equal fingerprints are "the same job".</summary>
    public static string Fingerprint(string invocationPayload, string contextJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(invocationPayload.Length + ":" + invocationPayload + contextJson)));

    /// <summary>
    /// Only a job still waiting (<c>Scheduled</c>) can be moved or replaced. One that is already enqueued, running or finished is left
    /// alone and the new schedule is a new occurrence (a reminder rescheduled after it fired is a new reminder).
    /// </summary>
    public static ScheduleAction Decide(string? existingState, bool sameFingerprint) =>
        existingState == ScheduledState ? (sameFingerprint ? ScheduleAction.Reschedule : ScheduleAction.Replace) : ScheduleAction.Create;
}
