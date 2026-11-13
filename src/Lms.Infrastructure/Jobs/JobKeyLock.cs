using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MySqlConnector;

namespace Lms.Infrastructure.Jobs;

/// <summary>
/// A named mutual-exclusion lock for scheduling one job key at a time. Hangfire's own MySQL distributed lock does not exclude callers
/// on different connections (verified by a probe test), so this uses the database's named locks, which hold across connections,
/// processes and API nodes.
/// </summary>
public interface IJobKeyLock
{
    /// <exception cref="TimeoutException">The lock was not free within <paramref name="timeout"/>.</exception>
    IDisposable Acquire(string resource, TimeSpan timeout);
}

/// <summary><c>GET_LOCK</c> on a dedicated connection per holder; released (and the connection returned) when disposed.</summary>
public sealed class MySqlJobKeyLock(string connectionString) : IJobKeyLock
{
    public IDisposable Acquire(string resource, TimeSpan timeout)
    {
        var name = LockName(resource);
        var connection = new MySqlConnection(connectionString);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT GET_LOCK(@name, @seconds)";
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@seconds", (int)Math.Ceiling(timeout.TotalSeconds));
            if (command.ExecuteScalar() is not (1 or 1L))
            {
                throw new TimeoutException($"The job key lock '{resource}' was not free within {timeout.TotalSeconds:0} s.");
            }

            return new Holder(connection, name);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>MySQL lock names are at most 64 characters: a fixed-length hash of the resource.</summary>
    public static string LockName(string resource) =>
        "lms:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resource)))[..56];

    private sealed class Holder(MySqlConnection connection, string name) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT RELEASE_LOCK(@name)";
                command.Parameters.AddWithValue("@name", name);
                command.ExecuteScalar();
            }
            finally
            {
                connection.Dispose(); // a pooled session must not keep a lock
            }
        }
    }
}

/// <summary>In-process fallback (tests that run without MySQL, single node): same contract, per-resource semaphore.</summary>
public sealed class InProcessJobKeyLock : IJobKeyLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public IDisposable Acquire(string resource, TimeSpan timeout)
    {
        var gate = _locks.GetOrAdd(resource, _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(timeout))
        {
            throw new TimeoutException($"The job key lock '{resource}' was not free within {timeout.TotalSeconds:0} s.");
        }

        return new Release(gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
